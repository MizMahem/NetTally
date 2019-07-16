﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NetTally.Utility;
using NetTally.Votes;

namespace NetTally.Forums
{
    /// <summary>
    /// Class to hold relevent post information when read from the forum.
    /// </summary>
    public class Post : IComparable<Post>, IEquatable<Post>, IComparable
    {
        #region Properties and Construction
        /// <summary>
        /// The post's origin (author, post ID, site, etc)
        /// </summary>
        public Origin Origin { get; }
        /// <summary>
        /// The text of the post.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Flag whether or not this post contains vote information.
        /// </summary>
        public bool IsVote => VoteLines.Count > 0;
        /// <summary>
        /// Any vote lines found in the post's text.
        /// </summary>
        public IReadOnlyList<VoteLine> VoteLines { get; }
        /// <summary>
        /// Vote lines after processing to expand plans within the original vote, and remove proposed plans.
        /// The <see cref="VoteLine"/> is a normal line, while the <see cref="VoteLineBlock"/> represents a complete plan.
        /// The <see cref="WorkingVote"/> is a sequence of one or the other.
        /// </summary>
        public List<(VoteLine? line, VoteLineBlock? block)> WorkingVote { get; } = new List<(VoteLine? line, VoteLineBlock? block)>();

        /// <summary>
        /// Flag whether the WorkingVote has been completely filled in.
        /// </summary>
        public bool WorkingVoteComplete { get; set; }
        /// <summary>
        /// Flag whether this post has been processed.
        /// </summary>
        public bool Processed { get; set; }
        /// <summary>
        /// Flag to bypass process restrictions, if normal processing doesn't happen.
        /// </summary>
        public bool ForceProcess { get; set; }

        /// <summary>
        /// Constructor for the a new post.
        /// Stores the post data, and extracts any vote lines if this isn't a tally post.
        /// </summary>
        /// <param name="author">The author of the post.</param>
        /// <param name="postId">The ID of the post.</param>
        /// <param name="text">The text of the post.</param>
        /// <param name="number">The thread post number.</param>
        public Post(Origin origin, string text)
        {
            Origin = origin ?? throw new ArgumentNullException(nameof(origin));
            Text = text ?? throw new ArgumentNullException(nameof(text));
            VoteLines = GetPostAnalysisResults(Text);
        }
        #endregion

        #region Private analysis of post
        // A post with ##### at the start of one of the lines is a posting of tally results.
        readonly static Regex tallyRegex = new Regex(@"^#####", RegexOptions.Multiline);
        // A line solely composed of a callout to a given user is used for nomination tallying.
        readonly static Regex nominationLineRegex = new Regex(@"^『url=""[^""]+?/members/\d+/""』@?(?<username>[^『]+)『/url』\s*$");

        /// <summary>
        /// Get the list of all found vote lines in the post's text content.
        /// If no normal votes are found, also check if this is a nomination post.
        /// </summary>
        /// <param name="text">Text of the post.</param>
        /// <returns>Returns a readonly list of any vote lines found.</returns>
        private IReadOnlyList<VoteLine> GetPostAnalysisResults(string text)
        {
            List<VoteLine> results = new List<VoteLine>();

            if (!IsTallyPost(text))
            {
                var postTextLines = text.GetStringLines();

                results.AddRange(GetVoteLines(postTextLines));

                if (results.Count == 0)
                {
                    results.AddRange(GetNominationVoteLines(postTextLines));
                }
            }

            return results;

            ////////////////////////////////
            // Private functions

            /// <summary>
            /// Determine if the provided post text is someone posting the results of a tally.
            /// </summary>
            /// <param name="postText">The text of the post to check.</param>
            /// <returns>Returns true if the post contains tally results.</returns>
            static bool IsTallyPost(string text)
            {
                // If the post contains the string "#####" at the start of the line for part of its text,
                // it's a tally post.
                string cleanText = VoteLineParser.StripBBCode(text);
                return (tallyRegex.Matches(cleanText).Count > 0);
            }

            /// <summary>
            /// Extracts vote lines from the text lines of the post.
            /// </summary>
            /// <param name="postTextLines">The lines from the post.</param>
            /// <returns>Returns an enumerable of all the vote lines found in the post text.</returns>
            static IEnumerable<VoteLine> GetVoteLines(List<string> postTextLines)
            {
                foreach (var line in postTextLines)
                {
                    var voteLine = VoteLineParser.ParseLine(line);

                    if (voteLine != null)
                        yield return voteLine;
                }
            }

            /// <summary>
            /// Examine the post to see if it qualifies as a nomination post — a post that contains nothing but username links.
            /// If so, return the nomination lines formatted as votes.
            /// </summary>
            /// <param name="postTextLines">The set of lines from the post.</param>
            /// <returns>Returns the list of nomination votes, or an empty list.</returns>
            static List<VoteLine> GetNominationVoteLines(List<string> postTextLines)
            {
                List<VoteLine> results = new List<VoteLine>();

                foreach (var line in postTextLines)
                {
                    Match m = nominationLineRegex.Match(line);
                    if (m.Success)
                    {
                        VoteLine voteLine = new VoteLine("", "X", "", m.Groups["username"].Value, MarkerType.Vote, 100);
                        results.Add(voteLine);
                    }
                    else
                    {
                        results.Clear();
                        return results;
                    }
                }

                return results;
            }
        }
        #endregion

        #region IComparable/IEquatable
#nullable disable
        public static int Compare(Post left, Post right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;

            return left.Origin.ID.CompareTo(right.Origin.ID);
        }

        public int CompareTo(Post other)
        {
            return Compare(this, other);
        }

        public int CompareTo(object obj)
        {
            return Compare(this, obj as Post);
        }

        public bool Equals(Post other)
        {
            return Compare(this, other) == 0;
        }

        public override bool Equals(object obj)
        {
            return Compare(this, obj as Post) == 0;
        }

        public override int GetHashCode()
        {
            return Origin.GetHashCode();
        }

        public static bool operator >(Post first, Post second) => Compare(first, second) == 1;
        public static bool operator <(Post first, Post second) => Compare(first, second) == -1;
        public static bool operator >=(Post first, Post second) => Compare(first, second) >= 0;
        public static bool operator <=(Post first, Post second) => Compare(first, second) <= 0;
        public static bool operator ==(Post first, Post second) => Compare(first, second) == 0;
        public static bool operator !=(Post first, Post second) => Compare(first, second) != 0;
#nullable enable
        #endregion
    }
}