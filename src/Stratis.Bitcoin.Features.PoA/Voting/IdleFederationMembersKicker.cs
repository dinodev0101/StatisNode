﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Features.PoA.Events;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public interface IIdleFederationMembersKicker : IDisposable
    {
        bool ShouldMemberBeKicked(PubKey pubKey, uint blockTime, out uint inactiveForSeconds);
        void Execute(ChainedHeader consensusTip);
        void Initialize();
    }

    /// <summary>
    /// Automatically schedules addition of voting data that votes for kicking federation member that
    /// didn't produce a block in <see cref="PoAConsensusOptions.FederationMemberMaxIdleTimeSeconds"/>.
    /// </summary>
    public class IdleFederationMembersKicker : IIdleFederationMembersKicker
    {
        private readonly ISignals signals;

        private readonly IKeyValueRepository keyValueRepository;

        private readonly IConsensusManager consensusManager;

        private readonly Network network;

        private readonly IFederationManager federationManager;

        private readonly ISlotsManager slotsManager;

        private readonly VotingManager votingManager;

        private readonly ILogger logger;

        private readonly IDateTimeProvider timeProvider;

        private readonly uint federationMemberMaxIdleTimeSeconds;

        private readonly PoAConsensusFactory consensusFactory;

        private SubscriptionToken blockConnectedToken, fedMemberAddedToken, fedMemberKickedToken;

        /// <summary>Active time is updated when member is added or produced a new block.</remarks>
        private Dictionary<PubKey, uint> fedPubKeysByLastActiveTime;

        private const string fedMembersByLastActiveTimeKey = "fedMembersByLastActiveTime";

        public IdleFederationMembersKicker(ISignals signals, Network network, IKeyValueRepository keyValueRepository, IConsensusManager consensusManager,
            IFederationManager federationManager, ISlotsManager slotsManager, VotingManager votingManager, ILoggerFactory loggerFactory, IDateTimeProvider timeProvider)
        {
            this.signals = signals;
            this.network = network;
            this.keyValueRepository = keyValueRepository;
            this.consensusManager = consensusManager;
            this.federationManager = federationManager;
            this.slotsManager = slotsManager;
            this.votingManager = votingManager;
            this.timeProvider = timeProvider;

            this.consensusFactory = this.network.Consensus.ConsensusFactory as PoAConsensusFactory;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.federationMemberMaxIdleTimeSeconds = ((PoAConsensusOptions)network.Consensus.Options).FederationMemberMaxIdleTimeSeconds;
        }

        /// <inheritdoc />
        public void Initialize()
        {
            this.blockConnectedToken = this.signals.Subscribe<BlockConnected>(this.OnBlockConnected);
            this.fedMemberAddedToken = this.signals.Subscribe<FedMemberAdded>(this.OnFedMemberAdded);
            this.fedMemberKickedToken = this.signals.Subscribe<FedMemberKicked>(this.OnFedMemberKicked);

            Dictionary<string, uint> loaded = this.keyValueRepository.LoadValueJson<Dictionary<string, uint>>(fedMembersByLastActiveTimeKey);

            if (loaded != null)
            {
                this.fedPubKeysByLastActiveTime = new Dictionary<PubKey, uint>();

                foreach (KeyValuePair<string, uint> loadedMember in loaded)
                {
                    this.fedPubKeysByLastActiveTime.Add(new PubKey(loadedMember.Key), loadedMember.Value);
                }
            }
            else
            {
                this.logger.LogDebug("No saved data found. Initializing federation data with current timestamp.");

                this.fedPubKeysByLastActiveTime = new Dictionary<PubKey, uint>();

                // Initialize with current timestamp. If we were to initialise with 0, then everyone would be wrong instantly!
                foreach (IFederationMember federationMember in this.federationManager.GetFederationMembers())
                    this.fedPubKeysByLastActiveTime.Add(federationMember.PubKey, (uint)this.timeProvider.GetAdjustedTimeAsUnixTimestamp());

                this.SaveMembersByLastActiveTime();
            }
        }

        /// <summary>
        /// This is to ensure that we keep <see cref="fedPubKeysByLastActiveTime"></see> up to date from other blocks being mined.
        /// </summary>
        /// <param name="blockConnected">The block that was connected.</param>
        private void OnBlockConnected(BlockConnected blockConnected)
        {
            UpdateFederationMembersLastActiveTime(blockConnected);
        }

        private void OnFedMemberKicked(FedMemberKicked fedMemberKickedData)
        {
            this.fedPubKeysByLastActiveTime.Remove(fedMemberKickedData.KickedMember.PubKey);

            this.SaveMembersByLastActiveTime();
        }

        private void OnFedMemberAdded(FedMemberAdded fedMemberAddedData)
        {
            if (!this.fedPubKeysByLastActiveTime.ContainsKey(fedMemberAddedData.AddedMember.PubKey))
            {
                this.fedPubKeysByLastActiveTime.Add(fedMemberAddedData.AddedMember.PubKey, this.consensusManager.Tip.Header.Time);

                this.SaveMembersByLastActiveTime();
            }
        }

        /// <inheritdoc />
        public bool ShouldMemberBeKicked(PubKey pubKey, uint blockTime, out uint inactiveForSeconds)
        {
            if (!this.fedPubKeysByLastActiveTime.TryGetValue(pubKey, out uint lastActiveTime))
            {
                inactiveForSeconds = 0;
                return false;
            }

            inactiveForSeconds = blockTime - lastActiveTime;

            // This might happen in test setup scenarios.
            if (blockTime < lastActiveTime)
                inactiveForSeconds = 0;

            return (inactiveForSeconds > this.federationMemberMaxIdleTimeSeconds && !this.federationManager.IsMultisigMember(pubKey));
        }

        /// <inheritdoc />
        public void Execute(ChainedHeader consensusTip)
        {
            // No member can be kicked at genesis.
            if (consensusTip.Height == 0)
                return;

            try
            {
                PubKey pubKey = this.slotsManager.GetFederationMemberForBlock(consensusTip, this.votingManager).PubKey;
                this.fedPubKeysByLastActiveTime.AddOrReplace(pubKey, consensusTip.Header.Time);
                this.SaveMembersByLastActiveTime();

                // Check if any fed member was idle for too long. Use the timestamp of the mined block.
                foreach (KeyValuePair<PubKey, uint> fedMemberToActiveTime in this.fedPubKeysByLastActiveTime)
                {
                    if (this.ShouldMemberBeKicked(fedMemberToActiveTime.Key, consensusTip.Header.Time, out uint inactiveForSeconds))
                    {
                        IFederationMember memberToKick = this.federationManager.GetFederationMembers().SingleOrDefault(x => x.PubKey == fedMemberToActiveTime.Key);

                        byte[] federationMemberBytes = this.consensusFactory.SerializeFederationMember(memberToKick);

                        bool alreadyKicking = this.votingManager.AlreadyVotingFor(VoteKey.KickFederationMember, federationMemberBytes);

                        if (!alreadyKicking)
                        {
                            this.logger.LogWarning("Federation member '{0}' was inactive for {1} seconds and will be scheduled to be kicked.", fedMemberToActiveTime.Key, inactiveForSeconds);

                            this.votingManager.ScheduleVote(new VotingData()
                            {
                                Key = VoteKey.KickFederationMember,
                                Data = federationMemberBytes
                            });
                        }
                        else
                        {
                            this.logger.LogDebug("Skipping because kicking is already voted for.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, ex.ToString());
                throw;
            }
        }

        private void UpdateFederationMembersLastActiveTime(BlockConnected blockConnected)
        {
            // The pubkey of the member that signed the block.
            PubKey key = this.slotsManager.GetFederationMemberForBlock(blockConnected.ConnectedBlock.ChainedHeader, this.votingManager).PubKey;

            // Update the dictionary.
            this.fedPubKeysByLastActiveTime.AddOrReplace(key, blockConnected.ConnectedBlock.ChainedHeader.Header.Time);

            // Save it back.
            this.SaveMembersByLastActiveTime();
        }

        private void SaveMembersByLastActiveTime()
        {
            var dataToSave = new Dictionary<string, uint>();

            foreach (KeyValuePair<PubKey, uint> pair in this.fedPubKeysByLastActiveTime)
                dataToSave.Add(pair.Key.ToHex(), pair.Value);

            this.keyValueRepository.SaveValueJson(fedMembersByLastActiveTimeKey, dataToSave);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.signals.Unsubscribe(this.blockConnectedToken);
            this.signals.Unsubscribe(this.fedMemberAddedToken);
            this.signals.Unsubscribe(this.fedMemberKickedToken);
        }
    }
}
