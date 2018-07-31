﻿using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.BlockPulling;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Configuration.Settings;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.Consensus.Interfaces;
using Stratis.Bitcoin.Features.Consensus.Rules;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

[assembly: InternalsVisibleTo("Stratis.Bitcoin.Features.Miner.Tests")]
[assembly: InternalsVisibleTo("Stratis.Bitcoin.Features.Consensus.Tests")]

namespace Stratis.Bitcoin.Features.Consensus
{
    public class ConsensusFeature : FullNodeFeature, INodeStats
    {
        private readonly DBreezeCoinView dBreezeCoinView;

        private readonly ICoinView coinView;

        private readonly IChainState chainState;

        private readonly IConnectionManager connectionManager;

        private readonly Signals.Signals signals;

        private readonly IConsensusManager consensusManager;

        private readonly NodeDeployments nodeDeployments;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Logger factory to create loggers.</summary>
        private readonly ILoggerFactory loggerFactory;

        /// <summary>Consensus statistics logger.</summary>
        private readonly ConsensusStats consensusStats;

        private IRuleRegistration ruleRegistration;

        private IConsensusRuleEngine consensusRuleEngine;

        public ConsensusFeature(
            DBreezeCoinView dBreezeCoinView,
            Network network,
            ICoinView coinView,
            IChainState chainState,
            IConnectionManager connectionManager,
            Signals.Signals signals,
            IConsensusManager consensusManager,
            IRuleRegistration ruleRegistration,
            IConsensusRuleEngine consensusRuleEngine,
            NodeDeployments nodeDeployments,
            ILoggerFactory loggerFactory,
            ConsensusStats consensusStats)
        {
            this.dBreezeCoinView = dBreezeCoinView;
            this.coinView = coinView;
            this.chainState = chainState;
            this.connectionManager = connectionManager;
            this.signals = signals;
            this.consensusManager = consensusManager;
            this.ruleRegistration = ruleRegistration;
            this.consensusRuleEngine = consensusRuleEngine;
            this.nodeDeployments = nodeDeployments;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.loggerFactory = loggerFactory;
            this.consensusStats = consensusStats;

            this.chainState.MaxReorgLength = network.Consensus.MaxReorgLength;
        }

        /// <inheritdoc />
        public void AddNodeStats(StringBuilder benchLogs)
        {
            if (this.chainState?.ConsensusTip != null)
            {
                benchLogs.AppendLine("Consensus.Height: ".PadRight(LoggingConfiguration.ColumnLength + 1) +
                                     this.chainState.ConsensusTip.Height.ToString().PadRight(8) +
                                     " Consensus.Hash: ".PadRight(LoggingConfiguration.ColumnLength - 1) +
                                     this.chainState.ConsensusTip.HashBlock);
            }
        }

        /// <inheritdoc />
        public override void Initialize()
        {
            DeploymentFlags flags = this.nodeDeployments.GetFlags(this.consensusManager.Tip);
            if (flags.ScriptFlags.HasFlag(ScriptVerify.Witness))
                this.connectionManager.AddDiscoveredNodesRequirement(NetworkPeerServices.NODE_WITNESS);


            this.consensusRuleEngine.Register(this.ruleRegistration);
            this.signals.SubscribeForBlocksConnected(this.consensusStats);
        }

        /// <summary>
        /// Prints command-line help.
        /// </summary>
        /// <param name="network">The network to extract values from.</param>
        public static void PrintHelp(Network network)
        {
            ConsensusSettings.PrintHelp(network);
        }

        /// <summary>
        /// Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            ConsensusSettings.BuildDefaultConfigurationFile(builder, network);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            var cache = this.coinView as CachedCoinView;
            if (cache != null)
            {
                this.logger.LogInformation("Flushing Cache CoinView...");
                cache.FlushAsync().GetAwaiter().GetResult();
                cache.Dispose();
            }

            this.dBreezeCoinView.Dispose();
        }
    }

    /// <summary>
    /// A class providing extension methods for <see cref="IFullNodeBuilder"/>.
    /// </summary>
    public static class FullNodeBuilderConsensusExtension
    {
        public static IFullNodeBuilder UsePowConsensus(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<ConsensusFeature>("consensus");
            LoggingConfiguration.RegisterFeatureClass<ConsensusStats>("bench");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                .AddFeature<ConsensusFeature>()
                .FeatureServices(services =>
                {
                    services.AddSingleton<ICheckpoints, Checkpoints>();
                    services.AddSingleton<ConsensusOptions, ConsensusOptions>();
                    services.AddSingleton<DBreezeCoinView>();
                    services.AddSingleton<ICoinView, CachedCoinView>();
                    services.AddSingleton<ConsensusController>();
                    services.AddSingleton<ConsensusStats>();
                    services.AddSingleton<ConsensusSettings>();
                    services.AddSingleton<IConsensusRuleEngine, PowConsensusRuleEngine>();
                    services.AddSingleton<IRuleRegistration, PowConsensusRulesRegistration>();
                });
            });

            return fullNodeBuilder;
        }

        public static IFullNodeBuilder UsePosConsensus(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<ConsensusFeature>("consensus");
            LoggingConfiguration.RegisterFeatureClass<ConsensusStats>("bench");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<ConsensusFeature>()
                    .FeatureServices(services =>
                    {
                        services.AddSingleton<ICheckpoints, Checkpoints>();
                        services.AddSingleton<DBreezeCoinView>();
                        services.AddSingleton<ICoinView, CachedCoinView>();
                        services.AddSingleton<StakeChainStore>().AddSingleton<IStakeChain, StakeChainStore>(provider => provider.GetService<StakeChainStore>());
                        services.AddSingleton<IStakeValidator, StakeValidator>();
                        services.AddSingleton<ConsensusController>();
                        services.AddSingleton<ConsensusStats>();
                        services.AddSingleton<ConsensusSettings>();
                        services.AddSingleton<IConsensusRuleEngine, PosConsensusRuleEngine>();
                        services.AddSingleton<IRuleRegistration, PosConsensusRulesRegistration>();
                    });
            });

            return fullNodeBuilder;
        }

        public class PowConsensusRulesRegistration : IRuleRegistration
        {
            public IEnumerable<ConsensusRule> GetRules()
            {
                return new List<ConsensusRule>
                {
                    // == Header ==
                    new HeaderTimeChecksRule(),
                    new CheckDifficultyPowRule(),
                    new BitcoinActivationRule(),

                    // == Integrity ==
                    new BlockMerkleRootRule(),

                    // == Partial ==
                    new SetActivationDeploymentsRule(),

                    // rules that are inside the method CheckBlockHeader

                    // rules that are inside the method ContextualCheckBlockHeader
                    new CheckpointsRule(),
                    new AssumeValidRule(),

                    // rules that are inside the method ContextualCheckBlock
                    new TransactionLocktimeActivationRule(), // implements BIP113
                    new CoinbaseHeightActivationRule(), // implements BIP34
                    new WitnessCommitmentsRule(), // BIP141, BIP144
                    new BlockSizeRule(),

                    // rules that are inside the method CheckBlock
                    new EnsureCoinbaseRule(),
                    new CheckPowTransactionRule(),
                    new CheckSigOpsRule(),

                    // == Full ==

                    // rules that require the store to be loaded (coinview)
                    new LoadCoinviewRule(),
                    new TransactionDuplicationActivationRule(), // implements BIP30
                    new PowCoinviewRule(), // implements BIP68, MaxSigOps and BlockReward calculation
                    new SaveCoinviewRule()
                };
            }
        }

        public class PosConsensusRulesRegistration : IRuleRegistration
        {
            public IEnumerable<ConsensusRule> GetRules()
            {
                return new List<ConsensusRule>
                {
                    // == Header ==
                    new HeaderTimeChecksRule(),
                    new HeaderTimeChecksPosRule(),
                    new StratisBigFixPosFutureDriftRule(),
                    new CheckDifficultyPosRule(),
                    new StratisHeaderVersionRule(),

                    // == Integrity ==
                    new BlockMerkleRootRule(),
                    new PosBlockSignatureRule(),

                    // == Partial ==
                    new SetActivationDeploymentsRule(),
                    new CheckDifficultykHybridRule(),
                    new PosTimeMaskRule(),

                    // rules that are inside the method CheckBlockHeader

                    // rules that are inside the method ContextualCheckBlockHeader
                    new CheckpointsRule(),
                    new AssumeValidRule(),

                    // rules that are inside the method ContextualCheckBlock
                    new TransactionLocktimeActivationRule(), // implements BIP113
                    new CoinbaseHeightActivationRule(), // implements BIP34
                    new WitnessCommitmentsRule(), // BIP141, BIP144
                    new BlockSizeRule(),

                    new PosBlockContextRule(), // TODO: this rule needs to be implemented

                    // rules that are inside the method CheckBlock
                    new EnsureCoinbaseRule(),
                    new CheckPowTransactionRule(),
                    new CheckPosTransactionRule(),
                    new CheckSigOpsRule(),
                    new PosCoinstakeRule(),

                    // == Full ==

                    // rules that require the store to be loaded (coinview)
                    new LoadCoinviewRule(),
                    new TransactionDuplicationActivationRule(), // implements BIP30
                    new PosCoinviewRule(), // implements BIP68, MaxSigOps and BlockReward calculation
                    new SaveCoinviewRule()
                };
            }
        }
    }
}