﻿using System;
using System.Collections;
using NetTally.Votes;

namespace NetTally.Comparers
{
    /// <summary>
    /// Custom sorting class for sorting votes.
    /// Sorts by Task+Content.
    /// </summary>
    public class CustomVoteSort : IComparer
    {
        public int Compare(object x, object y)
        {
            if (x is string xx && y is string yy)
            {
                if (ReferenceEquals(xx, yy))
                    return 0;

                string marker = VoteString.GetVoteMarker(xx);
                VoteType voteType = string.IsNullOrEmpty(marker) ? VoteType.Rank : VoteType.Plan;

                string compX = VoteString.GetVoteTask(xx, voteType) + " " + VoteString.GetVoteContent(xx, voteType);
                string compY = VoteString.GetVoteTask(yy, voteType) + " " + VoteString.GetVoteContent(yy, voteType);

                int result = string.Compare(compX, compY, StringComparison.CurrentCultureIgnoreCase);

                return result;
            }
            else if (x is Experiment3.VoteLineBlock xv && y is Experiment3.VoteLineBlock yv)
            {
                return xv.CompareTo(yv);
            }
            else
            {
                throw new ArgumentException("Parameters are not strings.");
            }
        }
    }
}
