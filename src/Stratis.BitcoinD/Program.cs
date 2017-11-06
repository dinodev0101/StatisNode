﻿using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Utilities;
using System;
using System.Threading.Tasks;

namespace Stratis.BitcoinD
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                NodeSettings nodeSettings = NodeSettings.FromArguments(args);

                var node = new FullNodeBuilder()
                    .UseNodeSettings(nodeSettings)
                    .UseConsensus()
                    .UseBlockStore()
                    .UseMempool()
                    .AddMining()
                    .AddRPC()
                    .Build();

                await node.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("There was a problem initializing the node. Details: '{0}'", ex.Message);
            }
        }
    }
}
