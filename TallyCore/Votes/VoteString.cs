﻿using System;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NetTally
{
    public static class VoteString
    {
        private static class VoteComponents
        {
            public static string Prefix { get; } = "prefix";
            public static string Marker { get; } = "marker";
            public static string Task { get; } = "task";
            public static string Content { get; } = "content";
        }


        static string markup = @"(\[/?[ibu]\]|\[color[^]]*\]|\[/color\])*";
        static string markups = $@"({markup}\s*)*";
        static readonly Regex getPrefixRegex = new Regex($@"^(?<m1>({markup}\s*)*)(?<prefix>-*)\s*(?={markup}\[\s*{markup}\s*[xX+✓✔1-9])(?<remainder>.*)");
        static readonly Regex getMarkerRegex = new Regex($@"^\s*(?<m1>({markup}\s*)*)(\[\s*(?<m2>({markup}\s*)*)(?<marker>[xX+✓✔1-9])\s*(?<m3>({markup}\s*)*)\])(?<remainder>.*)");
        static readonly Regex getTaskRegex = new Regex($@"^\s*(?<m1>({markup}\s*)*)\s*(\[\s*(?<m2>({markup}\s*)*)(?!url=)(?<task>[^\[\]]+)\s*(?<m3>({markup}\s*)*)\])?\s*(?<remainder>.*)");

        static readonly Regex getPartsRegex = new Regex($@"^(?<m1>{markups})(?<prefix>-*)(?<m2>{markups})\[\s*(?<m3>{markups})\s*(?<marker>[xX+✓✔1-9])\s*(?<m4>{markups})\s*\]\s*(?<m5>{markups})\s*(\[\s*(?<m6>{markups})\s*(?!url=)(?<task>[^\[\]]+)\s*(?<m7>{markups})\s*\])?\s*(?<content>.*)");

        // Regex to get the different parts of the vote. Content includes only the first line of the vote.
        static readonly Regex voteLineRegex = new Regex(@"^(?<prefix>-*)\s*\[\s*(?<marker>[xX+✓✔1-9])\s*\]\s*(\[\s*(?!url=)(?<task>[^]]*?)\s*\])?\s*(?<content>.*)");
        // Regex to match any markup that we'll want to remove during comparisons.
        static readonly Regex markupRegex = new Regex(@"\[/?[ibu]\]|\[color[^]]*\]|\[/color\]");
        // Regex to allow us to collapse a vote to a commonly comparable version.
        static readonly Regex collapseRegex = new Regex(@"\s|\.");
        // Regex to allow us to convert a vote's smart quote marks to a commonly comparable version.
        static readonly Regex quoteRegex = new Regex(@"[“”]");
        // Regex to allow us to convert a vote's apostrophe variations to a commonly comparable version.
        static readonly Regex aposRegex = new Regex(@"[ʼ‘’`]");
        // Regex to allow us to strip leading dashes from a per-line vote.
        static readonly Regex leadHyphenRegex = new Regex(@"^-+");
        // Regex for separating out the task from the other portions of a vote line.
        static readonly Regex taskRegex = new Regex(@"^(?<pre>.*?\[[xX+✓✔1-9]\])\s*(\[\s*(?!url=|color=|b\]|i\]|u\])(?<task>[^]]*?)\s*\])?\s*(?<remainder>.+)", RegexOptions.Singleline);
        // Potential reference to another user's plan.
        static readonly Regex referenceNameRegex = new Regex(@"^(?<label>(base\s*)?plan(:|\s)+)?(?<reference>.+)", RegexOptions.IgnoreCase);
        // Potential reference to another user's plan.
        static readonly Regex linkedReferenceRegex = new Regex(@"\[url=[^]]+\](.+)\[/url\]", RegexOptions.IgnoreCase);
        // Regex for extracting parts of the simplified condensed rank votes.
        static readonly Regex condensedVoteRegex = new Regex(@"^\[(?<task>[^]]*)\]\s*(?<content>.+)");




        /// <summary>
        /// Convert problematic characters to normalized versions so that comparisons can work.
        /// </summary>
        /// <param name="line">The vote line to normalize.</param>
        /// <returns>Returns the vote with punctuation normalized.</returns>
        public static string NormalizeVote(string line)
        {
            string normal = line;
            normal = quoteRegex.Replace(normal, "\"");
            normal = aposRegex.Replace(normal, "'");

            return normal;
        }

        /// <summary>
        /// Removes all BBCode from the vote line, for various comparison and analysis uses.
        /// </summary>
        /// <param name="line">The vote line to clean up.</param>
        /// <returns>Returns a normalized vote line with BBCode removed.</returns>
        public static string CleanVote(string line)
        {
            string cleaned = NormalizeVote(line);
            // Need to trim the result because removing markup may reveal new whitespace.
            cleaned = markupRegex.Replace(cleaned, "").Trim();

            return cleaned;
        }

        /// <summary>
        /// Collapse a vote to a minimized form, for comparison on keys.
        /// All BBCode markup is removed, along with all spaces and periods,
        /// and leading dashes when partitioning by line.  Smart quotes and
        /// apostrophes are converted to basic versions.  The text is then
        /// lowercased.
        /// </summary>
        /// <param name="voteLine">Original vote line to minimize.</param>
        /// <param name="quest">The quest being tallied.</param>
        /// <returns>Returns a minimized version of the vote string.</returns>
        public static string MinimizeVote(string voteLine)
        {
            string minimized = CleanVote(voteLine);
            minimized = collapseRegex.Replace(minimized, "");
            minimized = minimized.ToLower();
            minimized = leadHyphenRegex.Replace(minimized, "");

            return minimized;
        }
        
        /// <summary>
        /// Get the requested element out of the cleaned version of the vote line.
        /// </summary>
        /// <param name="line">The vote line in question.</param>
        /// <param name="element">The element (regex name) to extract.</param>
        /// <returns>Returns the requested element of the vote line, or an empty string if not found.</returns>
        private static string GetVoteElement(string line, string element)
        {
            string cleanLine = CleanVote(line);
            Match m = voteLineRegex.Match(cleanLine);
            if (m.Success)
            {
                return m.Groups[element]?.Value.Trim() ?? "";
            }

            return string.Empty;
        }

        /// <summary>
        /// Get the requested element out of the cleaned version of the condensed vote line.
        /// </summary>
        /// <param name="line">The condensed vote line in question.</param>
        /// <param name="element">The element (regex name) to extract.</param>
        /// <returns>Returns the requested element of the vote line, or an empty string if not found.</returns>
        private static string GetCondensedVoteElement(string line, string element)
        {
            string cleanLine = CleanVote(line);
            Match m = condensedVoteRegex.Match(cleanLine);
            if (m.Success)
            {
                return m.Groups[element]?.Value.Trim() ?? "";
            }

            return string.Empty;
        }

        /// <summary>
        /// Get the prefix of the vote line.
        /// </summary>
        /// <param name="line">The vote line being examined.</param>
        /// <returns>Returns the prefix of the vote line.</returns>
        public static string GetVotePrefix(string line) => GetVoteElement(line, VoteComponents.Prefix);

        /// <summary>
        /// Get the marker of the vote line.
        /// </summary>
        /// <param name="line">The vote line being examined.</param>
        /// <returns>Returns the marker of the vote line.</returns>
        public static string GetVoteMarker(string line) => GetVoteElement(line, VoteComponents.Marker);

        /// <summary>
        /// Get the (optional) task name from the provided vote line.
        /// If the vote type is specified to be Rank, use the regex that expects the condensed vote format.
        /// </summary>
        /// <param name="line">The vote line being examined.</param>
        /// <param name="voteType">Optional vote type.</param>
        /// <returns>Returns the name of the task, if found, or an empty string.</returns>
        public static string GetVoteTask(string line, VoteType voteType = VoteType.Vote)
        {
            if (voteType == VoteType.Rank)
            {
                return GetCondensedVoteElement(line, VoteComponents.Task);
            }

            return GetVoteElement(line, VoteComponents.Task);
        }

        /// <summary>
        /// Get the content of the vote line.
        /// An alternative function to get the content out of a vote that will attempt to
        /// use the condensed vote regex for rank votes.  If the vote type is not a ranked
        /// vote, it uses the default method.
        /// </summary>
        /// <param name="line">The vote text.</param>
        /// <param name="voteType">The type of vote.</param>
        /// <returns>Returns the content of this vote.</returns>
        public static string GetVoteContent(string line, VoteType voteType = VoteType.Vote)
        {
            if (voteType == VoteType.Rank)
            {
                return GetCondensedVoteElement(line, VoteComponents.Content);
            }

            return GetVoteElement(line, VoteComponents.Content);
        }

        /// <summary>
        /// Function to get all of the individual components of the vote line at once, including
        /// embedded BBCode (applied only to the content).
        /// </summary>
        /// <param name="line">The vote line to analyze.</param>
        /// <param name="prefix">The prefix (if any) for the vote line.</param>
        /// <param name="marker">The marker for the vote line.</param>
        /// <param name="task">The task (if any) for the vote line.</param>
        /// <param name="content">The content of the vote line.</param>
        public static void GetVoteComponents(string line, out string prefix, out string marker, out string task, out string content)
        {
            line = NormalizeVote(line);

            Match mms = getPartsRegex.Match(line);
            if (mms.Success)
            {
                prefix = mms.Groups["prefix"].Value;
                marker = mms.Groups["marker"].Value;
                task = mms.Groups["task"].Value;
                content = $"{mms.Groups["m1"].Value}{mms.Groups["m2"].Value}{mms.Groups["m3"].Value}{mms.Groups["m4"].Value}{mms.Groups["m5"].Value}{mms.Groups["m6"].Value}{mms.Groups["m7"].Value}{mms.Groups["content"].Value}";
                return;
            }

            Match m1 = getPrefixRegex.Match(line);
            if (m1.Success)
            {
                prefix = m1.Groups["prefix"].Value;

                string no_prefix = $"{m1.Groups["m1"].Value}{m1.Groups["remainder"].Value.Trim()}";

                Match m2 = getMarkerRegex.Match(no_prefix);

                if (m2.Success)
                {
                    marker = m2.Groups["marker"].Value;

                    string no_marker = $"{m2.Groups["m1"].Value}{m2.Groups["m2"].Value}{m2.Groups["m3"].Value}{m2.Groups["remainder"].Value.Trim()}";

                    Match m3 = getTaskRegex.Match(no_marker);

                    if (m3.Success)
                    {
                        task = m3.Groups["task"].Value;

                        string no_task = $"{m3.Groups["m1"].Value}{m3.Groups["m2"].Value}{m3.Groups["m3"].Value}{m3.Groups["remainder"].Value.Trim()}";

                        content = no_task;

                        return;
                    }
                }
            }

            throw new InvalidOperationException("Unable to parse vote line.");
        }



        /// <summary>
        /// Function to condense a rank vote to just the task and content of the original vote, for
        /// use in vote merging without needing to see all of the individual ranked votes.
        /// </summary>
        /// <param name="rankVote">The rank vote text.</param>
        /// <returns>Returns the vote condensed to just the [] task plus
        /// the vote content.  If there is no task, the [] is empty.</returns>
        public static string CondenseVote(string rankVote)
        {
            string task = GetVoteTask(rankVote);
            string content = GetVoteContent(rankVote);

            return $"[{task}] {content}";
        }

        /// <summary>
        /// Get whether the vote line is a ranked vote line (ie: marker uses digits 1-9).
        /// </summary>
        /// <param name="voteLine">The vote line being examined.</param>
        /// <returns>Returns true if the vote marker is a digit, false if not.</returns>
        public static bool IsRankedVote(string voteLine)
        {
            string marker = GetVoteMarker(voteLine);

            if (marker == string.Empty)
                return false;

            return char.IsDigit(marker, 0);
        }

        /// <summary>
        /// Get the potential names of vote plans from the contents of a vote line.
        /// It takes the original, the original plus the plan marker, the original
        /// with any trailing period stripped off, and that version plus the plan
        /// marker character. The last two are optional, depending on any trailing
        /// period.
        /// </summary>
        /// <param name="voteLine">The vote line being examined.</param>
        /// <returns>Returns possible plan names from the vote line.</returns>
        public static List<string> GetVoteReferenceNames(string voteLine)
        {
            string contents = GetVoteContent(voteLine);
            List<string> results = new List<string>();

            Match m1 = linkedReferenceRegex.Match(contents);
            if (m1.Success)
            {
                // (1: pre)(2: [url=stuff] @?(3: inside) [/url])(4: post)
                string pattern = @"(.*?)(\[url=[^]]+\]@?(.+?)\[/url\])(.*)";
                string replacement = "$1$3$4";
                contents = Regex.Replace(contents, pattern, replacement);
            }

            Match m2 = referenceNameRegex.Match(contents);
            if (m2.Success)
            {
                string name = m2.Groups["reference"].Value;

                // [x] Plan Kinematics => Kinematics
                // [x] Plan Boom. => Boom.
                results.Add(name);

                // [x] Plan Kinematics => ◈Kinematics
                // [x] Plan Boom. => ◈Boom.
                results.Add($"{Utility.Text.PlanNameMarker}{name}");

                // [x] Plan Kinematics. => Kinematics
                // [x] Plan Boom. => Boom
                // [x] Plan Kinematics. => ◈Kinematics
                // [x] Plan Boom. => ◈Boom
                if (name.EndsWith("."))
                {
                    name = name.Substring(0, name.Length - 1);

                    results.Add(name);
                    results.Add($"{Utility.Text.PlanNameMarker}{name}");
                }
            }

            return results;
        }


        #region Creating and modifying votes
        /// <summary>
        /// Create a vote line composed of the provided components.
        /// </summary>
        /// <param name="prefix">The prefix for the line.</param>
        /// <param name="marker">The marker for the line.</param>
        /// <param name="task">The task the line should be grouped with.</param>
        /// <param name="content">The contents of the vote.</param>
        /// <returns>Returns a complete vote line.</returns>
        public static string CreateVoteLine(string prefix, string marker, string task, string content)
        {
            if (prefix == null)
                prefix = "";

            if (marker == null || marker == string.Empty)
                marker = "X";

            if (task == null)
                task = "";

            if (content == null || content == string.Empty)
                throw new ArgumentNullException(nameof(content));

            string line;

            if (task == string.Empty)
                line = $"{prefix}[{marker}] {content}";
            else
                line = $"{prefix}[{marker}][{task}] {content}";

            return line;
        }

        /// <summary>
        /// Create a vote line composed of the provided components.
        /// Parameters should be null for values that won't change.
        /// </summary>
        /// <param name="prefix">The prefix for the line.</param>
        /// <param name="marker">The marker for the line.</param>
        /// <param name="task">The task the line should be grouped with.</param>
        /// <param name="content">The contents of the vote.</param>
        /// <returns>Returns a complete vote line.</returns>
        public static string ModifyVoteLine(string voteLine, string prefix = null, string marker = null, string task = null, string content = null)
        {
            if (voteLine == null || voteLine == string.Empty)
                throw new ArgumentNullException(nameof(voteLine));

            // If all parameters are null, the vote line doesn't change.
            if (prefix == null && marker == null && task == null && content == null)
                return voteLine;

            string votePrefix;
            string voteMarker;
            string voteTask;
            string voteContent;

            // Use the original vote line value for any parameter that is null.
            GetVoteComponents(voteLine, out votePrefix, out voteMarker, out voteTask, out voteContent);

            prefix = prefix ?? votePrefix;
            marker = marker ?? voteMarker;
            task = task ?? voteTask;
            content = content ?? voteContent;

            string modifiedLine;

            if (task == string.Empty)
                modifiedLine = $"{prefix}[{marker}] {content}";
            else
                modifiedLine = $"{prefix}[{marker}][{task}] {content}";

            return modifiedLine;
        }

        /// <summary>
        /// Replace the task in a vote line without modifying any BBCode markup.
        /// </summary>
        /// <param name="voteLine">The original vote line.</param>
        /// <param name="newTask">The new task to apply.
        /// Null or an empty string removes any existing task.</param>
        /// <param name="voteType">The type of vote being modified.</param>
        /// <returns>Returns the vote line with the task replaced, or the original
        /// string if the original line couldn't be matched by the regex.</returns>
        public static string ReplaceTask(string voteLine, string newTask, VoteType voteType = VoteType.Vote)
        {
            if (voteType == VoteType.Rank)
            {
                Match mc = condensedVoteRegex.Match(voteLine);

                if (mc.Success)
                {
                    return $"[{newTask ?? ""}] {mc.Groups[VoteComponents.Content].Value}";
                }
            }

            Match m = taskRegex.Match(voteLine);
            if (m.Success)
            {
                StringBuilder sb = new StringBuilder();

                sb.Append(m.Groups["pre"].Value);

                if (newTask != null && newTask != string.Empty)
                {
                    sb.Append($"[{newTask}]");
                }

                sb.Append(" ");

                sb.Append(m.Groups["remainder"].Value);

                return sb.ToString();
            }

            return voteLine;
        }
        #endregion

    }
}
