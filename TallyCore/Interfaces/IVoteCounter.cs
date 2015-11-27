﻿using System.Collections.Generic;
using HtmlAgilityPack;

namespace NetTally
{
    public interface IVoteCounter
    {
        void TallyVotes(IQuest quest, List<HtmlDocument> pages);
        void TallyPosts(IQuest quest, List<PostComponents> posts);
        void Reset();

        void AddVote(IEnumerable<string> voteParts, string voter, string postID, VoteType voteType);

        bool Merge(string fromVote, string toVote, VoteType voteType);
        bool Join(List<string> voters, string voterToJoin, VoteType voteType);
        bool Rename(string oldVote, string newVote, VoteType voteType);
        bool Delete(string vote, VoteType voteType);

        Dictionary<string, HashSet<string>> GetVotesCollection(VoteType voteType);
        Dictionary<string, string> GetVotersCollection(VoteType voteType);

        List<string> GetVotesFromReference(string voteLine);

        string Title { get; set; }
        bool HasRankedVotes { get; }

        HashSet<string> PlanNames { get; }
        bool HasPlan(string planName);

        List<string> GetCondensedRankVotes();
        bool HasVote(string vote, VoteType voteType);
        bool HasVoter(string voterName, VoteType voteType);


        HashSet<string> ReferenceVoters { get; }
        Dictionary<string, string> ReferenceVoterPosts { get; }
        HashSet<string> ReferencePlanNames { get; }
        Dictionary<string, List<string>> ReferencePlans { get; }

        List<PostComponents> FutureReferences { get; }
    }
}
