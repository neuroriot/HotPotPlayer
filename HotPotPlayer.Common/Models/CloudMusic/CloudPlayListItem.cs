﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotPotPlayer.Models.CloudMusic
{
    public record CloudPlayListItem : PlayListItem
    {
        public Uri Cover2 { get; set; }
        public long BookCount { get; set; }
        public string Description { get; set; }
        public string PlId { get; set; }
        public bool Subscribed { get; set; }
        public long PlayCount { get; set; }
        public long TrackCount { get; set; }

        public override Uri Cover => Cover2;
    }
}
