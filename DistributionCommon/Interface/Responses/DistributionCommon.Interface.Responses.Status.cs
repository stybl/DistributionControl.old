﻿namespace DistributionCommon.Interface.Responses
{
    using System;
    using System.Collections.Generic;

    public sealed class Status : Base
    {
        public Status(Dictionary<int, NodeInfo> nodes, Dictionary<int, DistributedJob.Blueprint> jobs) : base()
        {
            this.Nodes = nodes;
            this.Jobs = jobs;
        }

        public Dictionary<int, NodeInfo> Nodes { get; private set; }

        public Dictionary<int, DistributedJob.Blueprint> Jobs { get; private set; }
    }
}
