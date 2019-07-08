﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using NetTally.Utility;
using NetTally.Votes;
using NetTally.Experiment3;


namespace NetTally.VoteCounting
{
    public class VoteCounter : IVoteCounter
    {
        #region private collections
        readonly Dictionary<string, string> cleanVoteLookup = new Dictionary<string, string>();
        readonly Dictionary<string, string> cleanedKeys = new Dictionary<string, string>();
        readonly MergeRecords userMerges = new MergeRecords();
        readonly List<Post> postsList = new List<Post>();
        bool voteCounterIsTallying = false;

        Stack<UndoAction> UndoBuffer { get; } = new Stack<UndoAction>();

        HashSet<string> PlanNames { get; set; } = new HashSet<string>(Agnostic.StringComparer);
        HashSet<string> ReferencePlanNames { get; set; } = new HashSet<string>(Agnostic.StringComparer);
        Dictionary<string, string> PlanMessageId { get; } = new Dictionary<string, string>(Agnostic.StringComparer);
        Dictionary<string, VoteLineBlock> ReferencePlansEx3 { get; set; } = new Dictionary<string, VoteLineBlock>(Agnostic.StringComparer);
        HashSet<string> ReferenceVoters { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> VoterMessageId { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> ReferenceVoterPosts { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);


        // Deprecated collections:
        Dictionary<string, List<string>> ReferencePlans { get; set; } = new Dictionary<string, List<string>>(Agnostic.StringComparer);
        Dictionary<string, string> RankedVoterMessageId { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> VotesWithSupporters { get; set; } = new Dictionary<string, HashSet<string>>(Agnostic.StringComparer);
        Dictionary<string, HashSet<string>> RankedVotesWithSupporters { get; set; } = new Dictionary<string, HashSet<string>>(Agnostic.StringComparer);

        #endregion


        #region General Tally Properties
        /// <summary>
        /// The quest the vote counter is set to track.
        /// </summary>
        public IQuest? Quest { get; set; } = null;

        /// <summary>
        /// The title of the quest thread when tallied.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Flag whether the tally is currently running.
        /// </summary>
        public bool VoteCounterIsTallying
        {
            get { return voteCounterIsTallying; }
            set
            {
                if (voteCounterIsTallying != value)
                {
                    voteCounterIsTallying = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Track whether a tally was cancelled.
        /// </summary>
        public bool TallyWasCanceled { get; set; }

        /// <summary>
        /// Check whether there are any stored undo actions.
        /// </summary>
        public bool HasUndoActions => UndoBuffer.Count > 0;




        public Dictionary<VoteLineBlock, Dictionary<string, HashSet<VoteLineBlock>>> VoteBlockSupporters { get; private set; }
            = new Dictionary<VoteLineBlock, Dictionary<string, HashSet<VoteLineBlock>>>();



        public HashSet<Post> FutureReferences { get; } = new HashSet<Post>();

        public bool HasRankedVotes => RankedVotesWithSupporters.Count > 0;


        #endregion

        #region Basic reset & tally
        /// <summary>
        /// Reset all tracking variables.
        /// </summary>
        public void Reset()
        {
            VotesWithSupporters.Clear();
            VoterMessageId.Clear();
            RankedVotesWithSupporters.Clear();
            RankedVoterMessageId.Clear();
            PlanNames.Clear();

            VoteBlockSupporters.Clear();

            ReferenceVoters.Clear();
            ReferenceVoterPosts.Clear();
            ReferencePlanNames.Clear();
            ReferencePlans.Clear();
            ReferencePlansEx3.Clear();

            FutureReferences.Clear();

            UndoBuffer.Clear();

            OrderedTaskList.Clear();

            cleanVoteLookup.Clear();
            cleanedKeys.Clear();

            if (VotesWithSupporters.Comparer != Agnostic.StringComparer)
                VotesWithSupporters = new Dictionary<string, HashSet<string>>(Agnostic.StringComparer);
            if (RankedVotesWithSupporters.Comparer != Agnostic.StringComparer)
                RankedVotesWithSupporters = new Dictionary<string, HashSet<string>>(Agnostic.StringComparer);
            if (ReferencePlans.Comparer != Agnostic.StringComparer)
                ReferencePlans = new Dictionary<string, List<string>>(Agnostic.StringComparer);
            if (ReferencePlansEx3.Comparer != Agnostic.StringComparer)
                ReferencePlansEx3 = new Dictionary<string, VoteLineBlock>(Agnostic.StringComparer);
            if (ReferencePlanNames.Comparer != Agnostic.StringComparer)
                ReferencePlanNames = new HashSet<string>(Agnostic.StringComparer);
            if (PlanNames.Comparer != Agnostic.StringComparer)
                PlanNames = new HashSet<string>(Agnostic.StringComparer);

            OnPropertyChanged("VoteCounter");
            OnPropertyChanged("Tasks");
        }

        /// <summary>
        /// Reset user-defined tasks and user merges if the specified
        /// quest name is different than the one the vote counter has.
        /// </summary>
        /// <param name="forQuestName">The quest name that may have changed.</param>
        public void ResetUserDefinedTasks(string forQuestName)
        {
            if (Quest == null || Quest.DisplayName != forQuestName)
            {
                UserDefinedTasks.Clear();
                ResetUserMerges();
            }
        }

        /// <summary>
        /// Reset any merges the user has made.
        /// </summary>
        public void ResetUserMerges()
        {
            userMerges.Reset();
        }

        #endregion

        #region Handling Posts
        /// <summary>
        /// The list of posts collected from the quest. Read-only.
        /// </summary>
        public IReadOnlyList<Post> Posts => postsList;

        /// <summary>
        /// Add a new set of posts for the <see cref="IVoteCounter"/> to use.
        /// </summary>
        /// <param name="posts">The posts to be stored in the <see cref="IVoteCounter"/>.</param>
        public void AddPosts(IEnumerable<Post> posts)
        {
            postsList.Clear();
            if (posts != null)
                postsList.AddRange(posts);
        }

        /// <summary>
        /// Request that the currently stored posts be cleared.
        /// </summary>
        public void ClearPosts()
        {
            postsList.Clear();
        }
        #endregion

        #region Plan and Voter References
        /// <summary>
        /// Store a plan, to allow it to be looked up by plan name or post ID.
        /// </summary>
        /// <param name="planName">The name of the plan.</param>
        /// <param name="postID">The post ID the plan was defined in.</param>
        /// <param name="planBlock">The the vote line block that defines the plan.</param>
        /// <returns>Returns true if it was added, or false if it already exists.</returns>
        public bool AddPlanReference(string planName, string postID, VoteLineBlock planBlock)
        {
            if (!ReferencePlanNames.Contains(planName, Agnostic.StringComparer))
            {
                ReferencePlanNames.Add(planName);
                PlanMessageId[planName] = postID;
                ReferencePlansEx3[planName] = planBlock;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Store a voter and their post ID.
        /// </summary>
        /// <param name="voterName">The proper name of the voter.</param>
        /// <param name="postID">The ID of their vote post.</param>
        /// <returns>Returns true if the voter was added, or false if the voter already exists.</returns>
        public bool AddVoterReference(string voterName, string postID)
        {
            ReferenceVoterPosts[voterName] = postID;
            return ReferenceVoters.Add(voterName);
        }

        /// <summary>
        /// Add a post to a store of future references made.
        /// </summary>
        /// <param name="post">The post to store.</param>
        /// <returns>Returns true if the post was added, or false if it already exists.</returns>
        public bool AddFutureReference(Post post)
        {
            return FutureReferences.Add(post);
        }
        #endregion

        #region Get Reference Data
        /// <summary>
        /// Get canonical version of the provided voter name.
        /// </summary>
        /// <param name="voterName">The name of the voter being checked for.</param>
        /// <returns>Returns the reference version of the requested name, or null if not found.</returns>
        public string? GetVoterProperName(string voterName)
        {
            return ReferenceVoters.FirstOrDefault(v => Agnostic.StringComparer.Equals(v, voterName));
        }

        /// <summary>
        /// Get the post ID stored for the specified voter.  This will always be the last one entered.
        /// </summary>
        /// <param name="voterName">The name of the voter to check on.</param>
        /// <returns>Returns the post ID for the voter, or null if not found.</returns>
        public string? GetFinalVoterPostId(string voterName)
        {
            return ReferenceVoterPosts[voterName];
        }

        /// <summary>
        /// Get canonical version of the provided plan name.
        /// </summary>
        /// <param name="planName">The name of the plan being checked for.</param>
        /// <returns>Returns the reference version of the requested name, or null if not found.</returns>
        public string? GetPlanProperName(string planName)
        {
            return PlanNames.FirstOrDefault(n => Agnostic.InsensitiveComparer.Equals(n, planName));
        }

        /// <summary>
        /// Get the post ID stored for the specified plan, which is the post that it was defined in.
        /// </summary>
        /// <param name="planName">The name of the plan to check on.</param>
        /// <returns>Returns the post ID for where the plan was defined, or null if not found.</returns>
        public string? GetPlanPostId(string planName)
        {
            if (PlanMessageId.TryGetValue(planName, out string postId))
            {
                return postId;
            }

            return null;
        }

        /// <summary>
        /// Get a list of all vote blocks supported by a specified voter (which may be a plan name).
        /// </summary>
        /// <param name="voterName">The name of the voter or plan being requested.</param>
        /// <returns>Returns a list of all vote blocks supported by the specified voter or plan.</returns>
        public List<VoteLineBlock> GetVotesBy(string voterName)
        {
            var voteInstances = VoteBlockSupporters.Values.SelectMany(a => a[voterName]).ToList();
            return voteInstances;
        }

        /// <summary>
        /// Get the last post made by a given author.
        /// Possibly restrict the search range to no more than the specified post ID.
        /// </summary>
        /// <param name="voterName">The voter being queried.</param>
        /// <param name="maxPostId">The highest post ID allowed. 0 means unrestricted.</param>
        /// <returns>Returns the last post by the requested author, if found. Otherwise null.</returns>
        public Post? GetLastPostByAuthor(string author, int maxPostId = 0)
        {
            if (!HasReferenceVoter(author))
                return null;

            var lastAuthorPost = postsList.Where(p =>
                                                Agnostic.StringComparer.Equals(author, p.Author)
                                                && (maxPostId == 0 || p.IDValue < maxPostId))
                .OrderBy(p => p.IDValue).LastOrDefault();

            return lastAuthorPost;
        }
        #endregion

        #region Query if counter Has ...
        /// <summary>
        /// Determine if the requested plan name exists in the current list of plans.
        /// Applies the plan name marker character to the provided plan name, if it
        /// doesn't already exist.
        /// </summary>
        /// <param name="planName">The name of the plan to check for.</param>
        /// <returns>Returns whether the provided plan name exists in the current PlanNames hash set.</returns>
        public bool HasPlan(string? planName)
        {
            if (string.IsNullOrEmpty(planName))
                return false;

            return PlanNames.Contains(planName, Agnostic.StringComparer);
        }

        /// <summary>
        /// Check to see whether the specified vote has been recorded.
        /// Ranking votes are checked against their condensed forms.
        /// </summary>
        /// <param name="vote">The vote to check.</param>
        /// <param name="voteType">The type of vote being checked.</param>
        /// <returns>Returns true if found.</returns>
        public bool HasVote(string vote, VoteType voteType)
        {
            if (voteType == VoteType.Rank)
                return HasCondensedRankVote(vote);

            var votes = GetVotesCollection(voteType);
            return votes.ContainsKey(vote);
        }

        /// <summary>
        /// Check to see whether the specified vote has been recorded.
        /// Ranking votes are checked against their condensed forms.
        /// </summary>
        /// <param name="vote">The vote to check.</param>
        /// <returns>Returns true if found.</returns>
        public bool HasVote(VoteLineBlock vote)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Check to see whether the specified voter has been recorded.
        /// </summary>
        /// <param name="voterName">The voter to check for.</param>
        /// <param name="voteType">The type of vote being checked.</param>
        /// <returns>Returns true if found.</returns>
        public bool HasVoter(string voterName, VoteType voteType)
        {
            var voters = GetVotersCollection(voteType);
            return voters.Keys.Contains(voterName);
        }

        /// <summary>
        /// Check to see whether the specified voter has been recorded.
        /// </summary>
        /// <param name="voterName">The voter to check for.</param>
        /// <returns>Returns true if found.</returns>
        public bool HasVoter(string voterName)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Determines if the specified voter is in the list of all possible voters for the tally.
        /// </summary>
        /// <param name="voterName">The name of the voter to check for.</param>
        /// <returns>Returns true if the voter has voted in the current tally.</returns>
        public bool HasReferenceVoter(string? voterName)
        {
            if (voterName is null)
                return false;

            return ReferenceVoters.Contains(voterName, Agnostic.StringComparer);
        }

        /// <summary>
        /// Determines whether the authof of this post has made a newer vote
        /// submission.
        /// </summary>
        /// <param name="post">The post being checked.</param>
        /// <returns>Returns true if the voter has a newer vote
        /// already submitted to the counter.</returns>
        public bool HasNewerVote(Post post)
        {
            if (post == null)
                throw new ArgumentNullException(nameof(post));

            if (!HasVoter(post.Author, VoteType.Vote))
                return false;

            if (!int.TryParse(GetVotersCollection(VoteType.Vote)[post.Author], out int submittedID))
            {
                return string.CompareOrdinal(GetVotersCollection(VoteType.Vote)[post.Author], post.ID) > 0;
            }

            return (submittedID > post.IDValue);
        }
        #endregion

        #region Adding and Modifying Votes

        /// <summary>
        /// Add a collection of votes to the vote counter.
        /// </summary>
        /// <param name="voteParts">A string list of all the parts of the vote to be added.</param>
        /// <param name="voter">The voter for this vote.</param>
        /// <param name="postID">The post ID for this vote.</param>
        /// <param name="voteType">The type of vote being added.</param>
        public void AddVotes(IEnumerable<VoteLineBlock> voteParts, string voter, string postID, VoteType voteType)
        {
            if (!voteParts.Any())
                return;

            // Store/update the post ID of the voter
            AddVoterPostID(voter, postID, voteType);

            // Track plan names
            if (voteType == VoteType.Plan)
            {
                PlanNames.Add(voter);
            }

            // Remove the voter from any existing votes
            if (RemoveSupport(voter, voteType))
                OnPropertyChanged("Voters");

            // Add/update all segments of the provided vote
            foreach (var part in voteParts)
            {
                AddVote(part, voter, voteType);
            }

            // Cleanup any votes that no longer have any support
            if (CleanupEmptyVotes(voteType))
                OnPropertyChanged("Votes");

        }

        /// <summary>
        /// Adds an individual vote.
        /// </summary>
        /// <param name="vote">The vote that is being added to.</param>
        /// <param name="voter">The voter that is supporting the vote.</param>
        /// <param name="voteType">Type of the vote.</param>
        /// <exception cref="System.ArgumentNullException">vote and voter must not be null or empty.</exception>
        private void AddVote(VoteLineBlock vote, string voter, VoteType voteType)
        {
            var votes = VoteBlockSupporters;

            if (!votes.TryGetValue(vote, out var supporters))
            {
                supporters = new Dictionary<string, HashSet<VoteLineBlock>>(StringComparer.OrdinalIgnoreCase);
                votes.Add(vote, supporters);
            }

            if (!supporters.TryGetValue(voter, out var voterVotes))
            {
                voterVotes = new HashSet<VoteLineBlock>();
                supporters.Add(voter, voterVotes);
                OnPropertyChanged("Voters");
            }

            voterVotes.Add(vote);
            OnPropertyChanged("Votes");
        }

        /// <summary>
        /// Merge the vote supporters from one vote into another.
        /// </summary>
        /// <param name="fromVote">The originating vote.</param>
        /// <param name="toVote">The destination vote.</param>
        /// <returns>Returns true if successfully completed.</returns>
        public bool Merge(VoteLineBlock fromVote, VoteLineBlock toVote)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Merge the vote supporters from one vote into several other votes.
        /// </summary>
        /// <param name="fromVote">The originating vote.</param>
        /// <param name="toVotes">The destination votes.</param>
        /// <returns>Returns true if successfully completed.</returns>
        public bool Split(VoteLineBlock fromVote, List<VoteLineBlock> toVotes)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Shift support by various voters from their original vote to any votes
        /// supported by a specified target voter.
        /// </summary>
        /// <param name="voters">The voters that will support the new voter.</param>
        /// <param name="voterToJoin">The voter to join.</param>
        /// <returns>Returns true if successfully completed.</returns>
        public bool Join(List<string> voters, string voterToJoin)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Delete an entire vote and all associated supporters.
        /// </summary>
        /// <param name="vote">The vote to delete.</param>
        /// <returns>Returns true if successfully completed.</returns>
        public bool Delete(VoteLineBlock vote)
        {
            throw new NotImplementedException();
        }
        #endregion

        #region Modifying Votes
        /// <summary>
        /// Add a collection of votes to the vote counter.
        /// </summary>
        /// <param name="voteParts">A string list of all the parts of the vote to be added.</param>
        /// <param name="voter">The voter for this vote.</param>
        /// <param name="postID">The post ID for this vote.</param>
        /// <param name="voteType">The type of vote being added.</param>
        public void AddVotes(IEnumerable<string> voteParts, string voter, string postID, VoteType voteType)
        {
            if (voteParts == null)
                throw new ArgumentNullException(nameof(voteParts));
            if (string.IsNullOrEmpty(voter))
                throw new ArgumentNullException(nameof(voter));
            if (string.IsNullOrEmpty(postID))
                throw new ArgumentNullException(nameof(postID));

            if (!voteParts.Any())
                return;

            // Store/update the post ID of the voter
            AddVoterPostID(voter, postID, voteType);

            // Track plan names
            if (voteType == VoteType.Plan)
            {
                PlanNames.Add(voter);
            }

            // Remove the voter from any existing votes
            if (RemoveSupport(voter, voteType))
                OnPropertyChanged("Voters");

            // Add/update all segments of the provided vote
            foreach (var part in voteParts)
            {
                AddVote(part, voter, voteType);
            }

            // Cleanup any votes that no longer have any support
            if (CleanupEmptyVotes(voteType))
                OnPropertyChanged("Votes");

        }

        /// <summary>
        /// Adds an individual vote.
        /// </summary>
        /// <param name="vote">The vote that is being added to.</param>
        /// <param name="voter">The voter that is supporting the vote.</param>
        /// <param name="voteType">Type of the vote.</param>
        /// <exception cref="System.ArgumentNullException">vote and voter must not be null or empty.</exception>
        private void AddVote(string vote, string voter, VoteType voteType)
        {
            if (string.IsNullOrEmpty(vote))
                throw new ArgumentNullException(nameof(vote));
            if (string.IsNullOrEmpty(voter))
                throw new ArgumentNullException(nameof(voter));

            string voteKey = GetVoteKey(vote, voteType);
            var votes = GetVotesCollection(voteType);

            // Make sure there's a hashset for the voter list available for the vote key.
            if (votes.ContainsKey(voteKey) == false)
            {
                votes[voteKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                OnPropertyChanged("Votes");
            }

            string cleanVoter = VoteString.RemoveBBCode(voter);
            cleanVoter = VoteString.DeUrlContent(cleanVoter);

            // Update the supporters list if the voter isn't already in it.
            if (votes[voteKey].Contains(cleanVoter))
                return;

            votes[voteKey].Add(cleanVoter);

            OnPropertyChanged("Voters");
        }

        /// <summary>
        /// Adds the voter post identifier.
        /// </summary>
        /// <param name="voter">The voter.</param>
        /// <param name="postID">The post identifier.</param>
        /// <param name="voteType">Type of the vote.</param>
        /// <exception cref="System.ArgumentNullException">voter and postID may not be null or empty.</exception>
        private void AddVoterPostID(string voter, string postID, VoteType voteType)
        {
            if (string.IsNullOrEmpty(voter))
                throw new ArgumentNullException(nameof(voter));
            if (string.IsNullOrEmpty(postID))
                throw new ArgumentNullException(nameof(postID));

            string cleanVoter = VoteString.RemoveBBCode(voter);
            cleanVoter = VoteString.DeUrlContent(cleanVoter);

            // Store/update the post ID of the voter
            var voters = GetVotersCollection(voteType);
            voters[cleanVoter] = postID;
        }

        /// <summary>
        /// Merges the specified from vote into the specified to vote, assuming the votes aren't the same.
        /// Moves the voters from the from vote into the to vote list, and removes the 'from' vote's key.
        /// </summary>
        /// <param name="fromVote">Vote that is being merged.</param>
        /// <param name="toVote">Vote that is being merged into.</param>
        /// <param name="voteType">The type of vote being merged.</param>
        /// <returns>Returns true if there were any changes.</returns>
        public bool Merge(string fromVote, string toVote, VoteType voteType)
        {
            if (string.IsNullOrEmpty(fromVote))
                throw new ArgumentNullException(nameof(fromVote));
            if (string.IsNullOrEmpty(toVote))
                throw new ArgumentNullException(nameof(toVote));
            if (fromVote == toVote)
                return false;

            bool merged;

            if (voteType == VoteType.Rank)
                merged = MergeRanks(fromVote, toVote);
            else
                merged = MergeVotes(fromVote, toVote);

            if (merged)
            {
                OnPropertyChanged("VoteCounter");
            }

            return merged;
        }

        /// <summary>
        /// Merges the specified from vote into the specified to vote, assuming the votes aren't the same.
        /// Moves the voters from the from vote into the to vote list, and removes the from vote's key.
        /// </summary>
        /// <param name="fromVote">Vote that is being merged.</param>
        /// <param name="toVote">Vote that is being merged into.</param>
        /// <returns>Returns true if there were any changes.</returns>
        private bool MergeRanks(string fromVote, string toVote)
        {
            var votes = GetVotesCollection(VoteType.Rank);

            Dictionary<KeyValuePair<string, HashSet<string>>, string> mergedVotes = new Dictionary<KeyValuePair<string, HashSet<string>>, string>();

            foreach (var vote in votes)
            {
                if (VoteString.CondenseVote(vote.Key) == fromVote)
                {
                    string toContent = VoteString.GetVoteContent(toVote, VoteType.Rank);
                    string toTask = VoteString.GetVoteTask(toVote, VoteType.Rank);
                    string revisedKey = VoteString.ModifyVoteLine(vote.Key, task: toTask, content: toContent);

                    mergedVotes.Add(vote, revisedKey);
                }
            }

            if (mergedVotes.Count > 0)
            {
                var voters = GetVotersCollection(VoteType.Rank);
                UndoBuffer.Push(new UndoAction(UndoActionType.Merge, VoteType.Rank, voters, mergedVotes));

                foreach (var merge in mergedVotes)
                {
                    Rename(merge.Key, merge.Value, VoteType.Rank);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Merges the specified from vote into the specified to vote, assuming the votes aren't the same.
        /// Moves the voters from the from vote into the to vote list, and removes the from vote's key.
        /// </summary>
        /// <param name="fromVote">Vote that is being merged.</param>
        /// <param name="toVote">Vote that is being merged into.</param>
        /// <returns>Returns true if there were any changes.</returns>
        private bool MergeVotes(string fromVote, string toVote) => Rename(fromVote, toVote, VoteType.Vote);

        /// <summary>
        /// Rename a vote.
        /// </summary>
        /// <param name="oldVote">The old vote text.</param>
        /// <param name="newVote">The new vote text.</param>
        /// <param name="voteType">The type of vote.</param>
        /// <returns>Returns true if it renamed the vote.</returns>
        private bool Rename(string oldVote, string newVote, VoteType voteType)
        {
            var votes = GetVotesCollection(voteType);

            var oldVoteObj = votes.FirstOrDefault(v => v.Key == oldVote);

            return Rename(oldVoteObj, newVote, voteType);
        }

        /// <summary>
        /// Rename a vote.
        /// </summary>
        /// <param name="vote">The old vote object.</param>
        /// <param name="revisedKey">The new vote text.</param>
        /// <param name="voteType">The type of vote.</param>
        /// <returns>Returns true if it renamed the vote.</returns>
        private bool Rename(KeyValuePair<string, HashSet<string>> vote, string revisedKey, VoteType voteType)
        {
            if ((string.IsNullOrEmpty(vote.Key)) || (vote.Value == null))
                throw new ArgumentNullException(nameof(vote));
            if (revisedKey == null)
                throw new ArgumentNullException(nameof(revisedKey));
            if (revisedKey.Length == 0)
                throw new ArgumentException("New vote key is empty.", nameof(revisedKey));
            if (vote.Key == revisedKey)
                return false;
            if (Quest is null)
                throw new InvalidOperationException("Quest is null.");


            if (voteType != VoteType.Rank)
            {
                var voters = GetVotersCollection(voteType);
                UndoBuffer.Push(new UndoAction(UndoActionType.Merge, voteType, voters,
                    vote.Key, vote.Value, revisedKey, GetVoters(revisedKey, voteType)));
            }

            var votes = GetVotesCollection(voteType);

            if (votes.ContainsKey(revisedKey))
            {
                bool isRevisedSameAsVote = Agnostic.StringComparer.Equals(vote.Key, revisedKey);

                if (isRevisedSameAsVote)
                {
                    var priorVotes = vote.Value;
                    votes.Remove(vote.Key);
                    votes[revisedKey] = priorVotes;
                }
                else
                {
                    votes[revisedKey].UnionWith(vote.Value);
                    votes.Remove(vote.Key);
                }
            }
            else
            {
                votes[revisedKey] = vote.Value;
                votes.Remove(vote.Key);
            }

            userMerges.AddMergeRecord(vote.Key, revisedKey, Quest.PartitionMode);

            return true;
        }

        /// <summary>
        /// Merges voter support.
        /// All of the list of provided voters are adjusted to support the same votes
        /// as those supported by the voterToJoin.
        /// </summary>
        /// <param name="voters">List of voters that are being adjusted.</param>
        /// <param name="voterToJoin">Voter that all specified voters will be joining.</param>
        /// <param name="voteType">The type of vote being manipulated.</param>
        /// <returns>Returns true if adjustments were made.</returns>
        public bool Join(List<string> voters, string voterToJoin, VoteType voteType)
        {
            if (voters == null)
                throw new ArgumentNullException(nameof(voters));
            if (string.IsNullOrEmpty(voterToJoin))
                throw new ArgumentNullException(nameof(voterToJoin));
            if (voters.Count == 0)
                return false;

            var votes = GetVotesCollection(voteType);

            var joinVotersVotes = votes.Where(v => v.Value.Contains(voterToJoin));

            int count = 0;

            var priorVotes = votes.Where(v => v.Value.Any(u => voters.Contains(u)));
            var voterIDList = GetVotersCollection(voteType);
            UndoBuffer.Push(new UndoAction(UndoActionType.Join, voteType, voterIDList, voters, priorVotes));

            foreach (string voter in voters)
            {
                if (voter != voterToJoin)
                {
                    count++;
                    RemoveSupport(voter, voteType);

                    foreach (var vote in joinVotersVotes)
                    {
                        vote.Value.Add(voter);
                    }
                }
            }

            CleanupEmptyVotes(voteType);

            OnPropertyChanged("VoteCounter");

            return count > 0;
        }


        public bool Delete(string vote, VoteType voteType) => Delete(vote, voteType, suppressUndo: false);

        /// <summary>
        /// Delete a vote from the vote list specified.
        /// </summary>
        /// <param name="vote">The vote to remove.</param>
        /// <param name="voteType">The type of vote to remove.</param>
        /// <returns>Returns true if a vote was removed.</returns>
        public bool Delete(string vote, VoteType voteType, bool suppressUndo = false)
        {
            if (string.IsNullOrEmpty(vote))
                return false;

            bool removed = false;

            var votes = GetVotesCollection(voteType);
            Dictionary<string, HashSet<string>> deletedVotes = new Dictionary<string, HashSet<string>>();

            foreach (var v in votes)
            {
                if (v.Key == vote || (voteType == VoteType.Rank && VoteString.CondenseVote(v.Key) == vote))
                {
                    deletedVotes.Add(v.Key, v.Value);
                }
            }

            if (deletedVotes.Count > 0)
            {
                if (!suppressUndo)
                    UndoBuffer.Push(new UndoAction(UndoActionType.Delete, voteType, GetVotersCollection(voteType), deletedVotes));

                foreach (var del in deletedVotes)
                {
                    removed = votes.Remove(del.Key) || removed;
                }
            }

            foreach (var v in deletedVotes)
            {
                foreach (var voter in v.Value)
                {
                    TrimVoter(voter, voteType);
                }
            }

            OnPropertyChanged("VoteCounter");

            return removed;
        }

        /// <summary>
        /// TODO: Move this into the VoteConstructor class.
        /// Partitions the child components of a vote into separate vote entries, passing
        /// the voters into those child entries and removing the original.
        /// </summary>
        /// <param name="vote">The vote to partition.</param>
        /// <param name="voteType">The type of vote.</param>
        /// <returns>Returns true if the process was completed, or false otherwise.</returns>
        public bool PartitionChildren(string vote, VoteType voteType, VoteConstructor constructor)
        {
            if (string.IsNullOrEmpty(vote))
                return false;
            if (Quest is null)
                throw new InvalidOperationException("Quest is null.");

            // No point in partitioning rank votes
            if (voteType == VoteType.Rank)
            {
                return false;
            }

            // Make sure the provided vote exists
            string voteKey = GetVoteKey(vote, voteType);
            var votes = GetVotesCollection(voteType);

            if (votes.ContainsKey(voteKey) == false)
            {
                return false;
            }

            // Construct the new votes that the vote is being partitioned into.
            var voteLines = vote.GetStringLines();

            if (voteLines.Count < 2)
                return false;

            var afterVoteLines = voteLines.Skip(1);

            int indentCount = afterVoteLines.Min(a => VoteString.GetVotePrefix(a).Length);

            var promotedVoteLines = afterVoteLines.Select(a => a.Substring(indentCount)).ToList();

            var partitionedVotes = constructor.PartitionVoteStrings(promotedVoteLines, Quest, PartitionMode.ByBlock);

            HashSet<string> addedVotes = new HashSet<string>();

            var voters = votes[voteKey];
            bool votesChanged = false;

            foreach (var v in partitionedVotes)
            {
                var key = GetVoteKey(v, voteType);

                if (!votes.ContainsKey(key))
                {
                    votes[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    votesChanged = true;
                }

                votes[key].UnionWith(voters);

                addedVotes.Add(key);
            }

            UndoBuffer.Push(new UndoAction(UndoActionType.PartitionChildren, voteType, GetVotersCollection(voteType), addedVotes, voteKey, voters));

            Delete(vote, voteType, true);

            if (votesChanged)
                OnPropertyChanged("Votes");

            OnPropertyChanged("VoteCounter");

            return true;
        }

        /// <summary>
        /// Undoes the most recently performed modification to the vote count.
        /// </summary>
        /// <returns>Returns true if it performed an undo action.  Otherwise, false.</returns>
        public bool Undo()
        {
            if (!HasUndoActions)
                return false;
            if (Quest is null)
                throw new InvalidOperationException("Quest is null.");

            var undo = UndoBuffer.Pop();
            List<string> vote;

            switch (undo.ActionType)
            {
                case UndoActionType.Delete:
                    foreach (var v in undo.DeletedVotes)
                    {
                        foreach (var voter in v.Value)
                        {
                            AddVote(v.Key, voter, undo.VoteType);
                            AddVoterPostID(voter, undo.PostIDs[voter], undo.VoteType);
                        }
                    }
                    break;
                case UndoActionType.Merge:
                    if (undo.VoteType == VoteType.Rank)
                    {
                        foreach (var mergedVote in undo.MergedVotes)
                        {
                            userMerges.RemoveMergeRecord(mergedVote.Key.Key, mergedVote.Value, Quest.PartitionMode);

                            LimitVoters(mergedVote.Value, mergedVote.Key.Value, VoteType.Rank);

                            foreach (var voter in mergedVote.Key.Value)
                            {
                                AddVote(mergedVote.Key.Key, voter, undo.VoteType);
                                AddVoterPostID(voter, undo.PostIDs[voter], undo.VoteType);
                            }
                        }
                    }
                    else
                    {
                        userMerges.RemoveMergeRecord(undo.Vote1, undo.Vote2, Quest.PartitionMode);

                        LimitVoters(undo.Vote2, undo.Voters2, undo.VoteType);

                        vote = new List<string> { undo.Vote1 };
                        foreach (var voter in undo.Voters1)
                        {
                            AddVote(undo.Vote1, voter, undo.VoteType);
                            AddVoterPostID(voter, undo.PostIDs[voter], undo.VoteType);
                        }
                    }
                    break;
                case UndoActionType.Join:
                    foreach (string voter in undo.JoinedVoters)
                    {
                        RemoveSupport(voter, undo.VoteType);

                        foreach (var priorVote in undo.PriorVotes)
                        {
                            if (priorVote.Value.Contains(voter))
                            {
                                vote = new List<string> { priorVote.Key };

                                AddVote(priorVote.Key, voter, undo.VoteType);
                                AddVoterPostID(voter, undo.PostIDs[voter], undo.VoteType);
                            }
                        }
                    }
                    break;
                case UndoActionType.PartitionChildren:
                    var votes = GetVotesCollection(undo.VoteType);

                    foreach (var voter in undo.Voters1)
                    {
                        AddVote(undo.Vote1, voter, undo.VoteType);
                        AddVoterPostID(voter, undo.PostIDs[voter], undo.VoteType);

                        foreach (var addedVote in undo.Voters2)
                        {
                            if (votes.TryGetValue(addedVote, out HashSet<string> vs))
                                vs.Remove(voter);
                        }
                    }

                    break;
                default:
                    return false;
            }

            CleanupEmptyVotes(undo.VoteType);

            OnPropertyChanged("VoteCounter");

            return true;
        }

        private void LimitVoters(string vote2, HashSet<string> voters2, VoteType voteType)
        {
            var votes = GetVotesCollection(voteType);
            var vote = votes[vote2];

            if (voteType == VoteType.Rank)
            {
                vote.ExceptWith(voters2);
            }
            else
            {
                vote.IntersectWith(voters2);
            }
        }
        #endregion

        #region Private support methods
        /// <summary>
        /// Attempt to find any existing vote that matches with the vote we have,
        /// and can be used as a key in the VotesWithSupporters table.
        /// </summary>
        /// <param name="vote">The vote to search for.</param>
        /// <returns>Returns the string that can be used as a key in the VotesWithSupporters table.</returns>
        private string GetVoteKey(string vote, VoteType voteType)
        {
            if (Quest is null)
                throw new InvalidOperationException("Quest is null.");

            // Store a lookup of the cleaned version of the vote so we don't have to repeat the processing.
            if (!cleanedKeys.TryGetValue(vote, out string cleaned))
            {
                cleaned = VoteString.RemoveBBCode(vote);
                cleaned = VoteString.DeUrlContent(cleaned);
                cleanedKeys[vote] = cleaned;
            }

            var votes = GetVotesCollection(voteType);

            string returnVote = vote;

            // If the vote already matches an existing key, we don't need to search again.
            if (!votes.ContainsKey(vote))
            {
                // Find any vote that matches using an agnostic string comparison, that ignores
                // case, spacing, and most punctuation.
                string? agVote = votes.Keys.FirstOrDefault(k =>
                    Agnostic.StringComparer.Equals(cleaned, cleanedKeys[k]));

                // If we found a match, return that; otherwise this is a new vote, so return it unchanged.
                if (!string.IsNullOrEmpty(agVote))
                    returnVote = agVote;
            }

            if (userMerges.TryGetMergeRecord(returnVote, Quest.PartitionMode, out string mergedVoteKey))
            {
                if (!cleanedKeys.TryGetValue(mergedVoteKey, out string cleanMerge))
                {
                    cleanMerge = VoteString.RemoveBBCode(vote);
                    cleanMerge = VoteString.DeUrlContent(cleanMerge);
                    cleanedKeys[mergedVoteKey] = cleanMerge;
                }

                return mergedVoteKey;
            }

            return returnVote;
        }

        /// <summary>
        /// Remove the voter's support for any existing votes.
        /// </summary>
        /// <param name="voter">The voter name to check for.</param>
        /// <param name="voteType">Type of the vote.</param>
        private bool RemoveSupport(string voter, VoteType voteType)
        {
            bool removedAny = false;

            if (voteType != VoteType.Plan)
            {
                var votes = GetVotesCollection(voteType);

                // Remove the voter from any existing votes
                foreach (var vote in votes)
                {
                    if (vote.Value.Remove(voter))
                        removedAny = true;
                }
            }

            return removedAny;
        }

        /// <summary>
        /// Removes any votes that no longer have any voter support.
        /// </summary>
        /// <param name="voteType">Type of the vote.</param>
        private bool CleanupEmptyVotes(VoteType voteType)
        {
            bool removedAny = false;

            var votes = GetVotesCollection(voteType);

            // Any votes that no longer have any support can be removed
            var emptyVotes = votes.Where(v => v.Value.Count == 0).ToList();
            foreach (var emptyVote in emptyVotes)
            {
                if (votes.Remove(emptyVote.Key))
                    removedAny = true;
            }

            return removedAny;
        }

        /// <summary>
        /// Will remove the specified voter from the voter ID list if there are no
        /// votes that they are currently supporting.
        /// </summary>
        /// <param name="voter">The voter to trim.</param>
        /// <param name="voteType">The type of vote to check.</param>
        private void TrimVoter(string voter, VoteType voteType)
        {
            var votes = GetVotesCollection(voteType);
            var voters = GetVotersCollection(voteType);

            if (!votes.Values.Any(v => v.Contains(voter)))
            {
                voters.Remove(voter);
            }
        }

        #endregion

        #region Task properties
        public HashSet<string> VoteDefinedTasks { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> UserDefinedTasks { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<string> OrderedTaskList { get; } = new List<string>();

        /// <summary>
        /// Gets the known tallied and user-defined tasks.
        /// </summary>
        public IEnumerable<string> KnownTasks
        {
            get
            {
                var voteTasks = GetVotesCollection(VoteType.Vote).Keys
                    .Select(v => VoteString.GetVoteTask(v));
                var rankTasks = GetVotesCollection(VoteType.Rank).Keys
                    .Select(v => VoteString.GetVoteTask(v));
                var userTasks = UserDefinedTasks.ToList();

                var allTasks = voteTasks.Concat(rankTasks).Concat(userTasks)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(v => !string.IsNullOrEmpty(v));

                return allTasks;
            }
        }
        #endregion

        #region Implement INotifyPropertyChanged interface
        /// <summary>
        /// Event for INotifyPropertyChanged.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Function to raise events when a property has been changed.
        /// </summary>
        /// <param name="propertyName">The name of the property that was modified.</param>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion







        #region Query on collection stuff
        /// <summary>
        /// Get the dictionary collection of votes for the requested vote type.
        /// </summary>
        /// <param name="voteType">The type of vote being requested.</param>
        /// <returns>Returns a dictionary collection of the requested vote type.</returns>
        public Dictionary<string, HashSet<string>> GetVotesCollection(VoteType voteType)
        {
            if (voteType == VoteType.Rank)
                return RankedVotesWithSupporters;
            else
                return VotesWithSupporters;
        }

        /// <summary>
        /// Get the dictionary collection of voters and post IDs for the requested vote type.
        /// </summary>
        /// <param name="voteType">The type of vote being requested.</param>
        /// <returns>Returns a dictionary collection of the requested voter type.</returns>
        public Dictionary<string, string> GetVotersCollection(VoteType voteType)
        {
            if (voteType == VoteType.Rank)
                return RankedVoterMessageId;
            else
                return VoterMessageId;
        }

        private HashSet<string> GetVoters(string vote, VoteType voteType)
        {
            var votes = GetVotesCollection(voteType);

            if (votes.ContainsKey(vote))
                return votes[vote];

            return new HashSet<string>(Agnostic.StringComparer);
        }

        /// <summary>
        /// Find all votes tied to a given vote line.
        /// The "plan name" (possibly user name) is checked with the
        /// standard and alternate extractions (adding a special marker character
        /// depending on whether the word "plan" is used, and whether it's 
        /// standard or alt) in order to look up votes that said (possible) voter
        /// supports.
        /// </summary>
        /// <param name="voteLine">The vote line to be checked.</param>
        /// <param name="author">The author of the vote. Prevent circular references.</param>
        /// <returns>Returns a list of all votes supported by the user or plan
        /// specified in the vote line, if found.  Otherwise returns an
        /// empty list.</returns>
        public List<string> GetVotesFromReference(string voteLine, string author)
        {
            if (voteLine == null)
                throw new ArgumentNullException(nameof(voteLine));
            if (author == null)
                throw new ArgumentNullException(nameof(author));
            if (Quest is null)
                throw new InvalidOperationException("Quest is null.");

            List<string> results = new List<string>();

            var referenceNames = VoteString.GetVoteReferenceNames(voteLine);

            if (!referenceNames[ReferenceType.Any].Any())
                return results;

            string? proxyName = null;


            // Label ^ or '↑' means it must be a user reference.
            // Label 'plan' means it might be user or plan reference.

            if (referenceNames[ReferenceType.Label].Contains("plan"))
            {
                // If it's labeled as a 'plan', check plans first, then users.

                // If the reference exists as a plan, use it.
                if (referenceNames[ReferenceType.Plan].Any() && HasPlan(referenceNames[ReferenceType.Plan].First()))
                {
                    // Get the plan name from the recorded plan names collection.
                    proxyName = PlanNames.First(p => referenceNames[ReferenceType.Plan]
                        .Contains(VoteString.DeUrlContent(VoteString.RemoveBBCode(p)), Agnostic.StringComparer));
                }
                // If it doesn't exist as a plan, then we can check for users, as long as the quest hasn't disabled proxy votes.
                else if (!Quest.DisableProxyVotes &&
                        ReferenceVoters.Contains(referenceNames[ReferenceType.Voter].First(), Agnostic.InsensitiveComparer))
                {
                    proxyName = ReferenceVoters.First(n => referenceNames[ReferenceType.Voter].Contains(n, Agnostic.InsensitiveComparer));

                    // But don't allow self-references.  Some user names conflict with normal vote lines.
                    if (proxyName == author)
                        proxyName = null;
                }
            }
            else
            {
                // If it's not labeled as a 'plan', then it's either a pin or an unlabeled reference.
                // Either way, check for users first.

                // See if any voter names match the line (as long as we're allowing proxy votes).  If so, use that.
                if (!Quest.DisableProxyVotes &&
                    ReferenceVoters.Contains(referenceNames[ReferenceType.Voter].First(), Agnostic.InsensitiveComparer))
                {
                    proxyName = ReferenceVoters.First(n => referenceNames[ReferenceType.Voter].Contains(n, Agnostic.InsensitiveComparer));

                    // And make sure it's not the user using their own name.  Some user names conflict with normal vote lines.
                    if (proxyName == author)
                        proxyName = null;
                }
                // If a user isn't found, only check for regular plans if there's not a non-plan label being used,
                // but not if the quest requires plan vote lines to be labeled.
                else if (!Quest.ForcePlanReferencesToBeLabeled && !referenceNames[ReferenceType.Label].Any())
                {
                    if (referenceNames[ReferenceType.Plan].Any() && HasPlan(referenceNames[ReferenceType.Plan].First()))
                    {
                        // Get the plan name from the recorded plan names collection.
                        proxyName = PlanNames.First(p => referenceNames[ReferenceType.Plan]
                            .Contains(VoteString.DeUrlContent(VoteString.RemoveBBCode(p)), Agnostic.StringComparer));
                    }
                }
            }

            if (!string.IsNullOrEmpty(proxyName))
            {
                var planVotes = VotesWithSupporters.Where(v => v.Value.Contains(proxyName));

                if (planVotes.Count() > 1)
                    planVotes = VotesWithSupporters.Where(v => v.Value.Contains(proxyName, Agnostic.StringComparer));

                results.AddRange(planVotes.Select(v => v.Key));
            }

            return results;
        }

        /// <summary>
        /// Gets a list of ranking votes in condensed form.
        /// </summary>
        /// <returns>Returns a list of ranking votes in condensed form.</returns>
        public List<string> GetCondensedRankVotes()
        {
            var condensed = RankedVotesWithSupporters.Keys.Select(k => VoteString.CondenseVote(k)).Distinct().ToList();
            return condensed;
        }

        /// <summary>
        /// Determines whether the provided vote string can be found in
        /// condensed form in the rank votes.
        /// </summary>
        /// <param name="rankVote">The vote to check for.</param>
        /// <returns>Returns true if found.</returns>
        private bool HasCondensedRankVote(string rankVote)
        {
            foreach (var vote in RankedVotesWithSupporters)
            {
                if (VoteString.CondenseVote(vote.Key) == rankVote)
                    return true;
            }

            return false;
        }

        #endregion

        #region Deprecated
        #endregion

    }
}
