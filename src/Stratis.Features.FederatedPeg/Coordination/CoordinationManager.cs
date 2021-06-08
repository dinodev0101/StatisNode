﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json;
using NLog;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonConverters;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Payloads;

namespace Stratis.Features.FederatedPeg.Coordination
{
    public interface ICoordinationManager
    {
        /// <summary>
        /// Records a vote for a particular transactionId to be associated with the request.
        /// The vote is recorded against the pubkey of the federation member that cast it.
        /// </summary>
        /// <param name="requestId">The identifier of the request.</param>
        /// <param name="transactionId">The voted-for transactionId.</param>
        /// <param name="pubKey">The pubkey of the federation member that signed the incoming message.</param>
        void AddVote(string requestId, BigInteger transactionId, PubKey pubKey);

        /// <summary>
        /// If one of the transaction Ids being voted on has reached a quroum, this will return that transactionId.
        /// </summary>
        /// <param name="requestId">The identifier of the request.</param>
        /// <param name="quorum">The number of votes required for a majority.</param>
        /// <returns>The transactionId of the request that has reached a quorum of votes.</returns>
        BigInteger GetAgreedTransactionId(string requestId, int quorum);

        /// <summary>
        /// Returns the currently highest-voted transactionId.
        /// If there is a tie, one is picked randomly.
        /// </summary>
        /// <param name="requestId">The identifier of the request.</param>
        /// <returns>The transactionId of the highest-voted request.</returns>
        BigInteger GetCandidateTransactionId(string requestId);

        /// <summary>Checks if vote was received from the pubkey specified for a particular <see cref="ConversionRequest"/>.</summary>
        bool CheckIfVoted(string requestId, PubKey pubKey);

        /// <summary>Removes all votes associated with provided request Id.</summary>
        void RemoveTransaction(string requestId);

        /// <summary>Provides mapping of all request ids to pubkeys that have voted for them.</summary>
        Dictionary<string, HashSet<PubKey>> GetStatus();

        void RegisterQuorumSize(int quorum);

        int GetQuorum();

        Task<InteropConversionRequestFee> AgreeFeeForConversionRequestAsync(string requestId, int blockHeight);

        void MultiSigMemberProposedInteropFee(string requestId, ulong feeAmount, int blockHeight, PubKey pubKey);

        void MultiSigMemberAgreedOnInteropFee(string requestId, ulong feeAmount, int blockHeight, PubKey pubKey);
    }

    public sealed class CoordinationManager : ICoordinationManager
    {
        private readonly IExternalApiPoller externalApiPoller;
        private readonly IFederationManager federationManager;
        private readonly IFederatedPegBroadcaster federatedPegBroadcaster;
        private readonly IInteropFeeCoordinationKeyValueStore interopRequestKeyValueStore;
        private readonly ILogger logger;

        /// <summary> Interflux transaction ID votes </summary>
        private Dictionary<string, Dictionary<BigInteger, int>> activeVotes;
        private Dictionary<string, HashSet<PubKey>> receivedVotes;

        /// <summary> Proposed fees by request id. </summary>
        private Dictionary<string, List<InterOpFeeToMultisig>> feeProposalsByRequestId;

        /// <summary> Agreed fees by request id. </summary>
        private Dictionary<string, List<InterOpFeeToMultisig>> agreedFeeVotesByRequestId;

        private int quorum;

        private readonly object lockObject = new object();

        public CoordinationManager(
            IExternalApiPoller externalApiPoller,
            IFederationManager federationManager,
            IFederatedPegBroadcaster federatedPegBroadcaster,
            IInteropFeeCoordinationKeyValueStore interopRequestKeyValueStore,
            INodeStats nodeStats)
        {
            this.activeVotes = new Dictionary<string, Dictionary<BigInteger, int>>();
            this.receivedVotes = new Dictionary<string, HashSet<PubKey>>();

            this.feeProposalsByRequestId = new Dictionary<string, List<InterOpFeeToMultisig>>();
            this.agreedFeeVotesByRequestId = new Dictionary<string, List<InterOpFeeToMultisig>>();

            this.externalApiPoller = externalApiPoller;
            this.federationManager = federationManager;
            this.federatedPegBroadcaster = federatedPegBroadcaster;
            this.interopRequestKeyValueStore = interopRequestKeyValueStore;
            this.logger = LogManager.GetCurrentClassLogger();

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name);
        }

        /// <inheritdoc/>
        public async Task<InteropConversionRequestFee> AgreeFeeForConversionRequestAsync(string requestId, int blockHeight)
        {
            InteropConversionRequestFee interopConversionRequestFee;

            lock (this.lockObject)
            {
                interopConversionRequestFee = GetOrCreateInteropConversionRequestFeeLocked(requestId, blockHeight);

                // If the fee for this request has been proposed and agreed upon, then return
                // it back to the Matured Blocks Sync Manager so that the deposit
                // can be created.
            }

            if (interopConversionRequestFee.State == InteropFeeState.AgreeanceConcluded)
                return interopConversionRequestFee;

            do
            {
                // If the fee proposal has not concluded then continue until it has.
                if (interopConversionRequestFee.State == InteropFeeState.ProposalInProgress)
                {
                    SubmitProposalForInteropFeeForConversionRequest(interopConversionRequestFee);

                    // Execute a small delay to not flood the network with proposal requests.
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    continue;
                }

                if (interopConversionRequestFee.State == InteropFeeState.AgreeanceInProgress)
                {
                    AgreeOnInteropFeeForConversionRequest(interopConversionRequestFee);

                    // Execute a small delay to not flood the network with proposal requests.
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    continue;
                }

                if (interopConversionRequestFee.State == InteropFeeState.AgreeanceConcluded)
                    break;

            } while (true);

            return interopConversionRequestFee;
        }

        private InteropConversionRequestFee GetOrCreateInteropConversionRequestFeeLocked(string requestId, int blockHeight)
        {
            InteropConversionRequestFee interopConversionRequest;

            byte[] proposalBytes = this.interopRequestKeyValueStore.LoadBytes(requestId);
            if (proposalBytes != null)
            {
                string json = Encoding.ASCII.GetString(proposalBytes);
                interopConversionRequest = Serializer.ToObject<InteropConversionRequestFee>(json);
            }
            else
            {
                interopConversionRequest = new InteropConversionRequestFee() { RequestId = requestId, BlockHeight = blockHeight, State = InteropFeeState.ProposalInProgress };
                this.interopRequestKeyValueStore.SaveValueJson(requestId, interopConversionRequest);
            }

            return interopConversionRequest;
        }

        private void SubmitProposalForInteropFeeForConversionRequest(InteropConversionRequestFee interopConversionRequestFee)
        {
            lock (this.lockObject)
            {
                // If the request id doesn't exist, propose the fee and broadcast it.
                if (!this.feeProposalsByRequestId.TryGetValue(interopConversionRequestFee.RequestId, out List<InterOpFeeToMultisig> proposals))
                {
                    ulong candidateFee = (ulong)(this.externalApiPoller.EstimateConversionTransactionFee() * 100_000_000m);

                    this.logger.Debug($"No nodes has proposed a fee of {candidateFee} for conversion request id '{interopConversionRequestFee.RequestId}'.");
                    this.feeProposalsByRequestId.Add(interopConversionRequestFee.RequestId, new List<InterOpFeeToMultisig>() { new InterOpFeeToMultisig() { BlockHeight = interopConversionRequestFee.BlockHeight, PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(), FeeAmount = candidateFee } });
                }
                else
                {
                    if (!HasFeeProposalBeenConcluded(interopConversionRequestFee) && !proposals.Any(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex()))
                    {
                        ulong candidateFee = (ulong)(this.externalApiPoller.EstimateConversionTransactionFee() * 100_000_000m);

                        this.logger.Debug($"Adding proposed fee of {candidateFee} for conversion request id '{interopConversionRequestFee.RequestId}'.");
                        proposals.Add(new InterOpFeeToMultisig() { BlockHeight = interopConversionRequestFee.BlockHeight, PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(), FeeAmount = candidateFee });
                    }
                }
            }

            this.feeProposalsByRequestId.TryGetValue(interopConversionRequestFee.RequestId, out List<InterOpFeeToMultisig> processedProposals);

            this.logger.Debug($"{processedProposals.Count} node(s) has proposed a fee for conversion request id {interopConversionRequestFee.RequestId}.");

            if (HasFeeProposalBeenConcluded(interopConversionRequestFee))
            {
                // Update the proposal state and save it.
                interopConversionRequestFee.State = InteropFeeState.ProposalConcluded;
                this.interopRequestKeyValueStore.SaveValueJson(interopConversionRequestFee.RequestId, interopConversionRequestFee);

                IEnumerable<long> values = processedProposals.Select(s => Convert.ToInt64(s.FeeAmount));
                this.logger.Debug($"Proposal fee for request id '{interopConversionRequestFee.RequestId}' has concluded, average amount: {values.Average()}");
            }
            else
            {
                // If the proposal is not concluded, broadcast again.
                InterOpFeeToMultisig myProposal = processedProposals.First(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex());
                string signature = this.federationManager.CurrentFederationKey.SignMessage(interopConversionRequestFee.RequestId + myProposal.FeeAmount);

                this.federatedPegBroadcaster.BroadcastAsync(new FeeProposalPayload(interopConversionRequestFee.RequestId, myProposal.FeeAmount, interopConversionRequestFee.BlockHeight, signature)).GetAwaiter().GetResult();
            }
        }

        /// <inheritdoc/>
        public void MultiSigMemberProposedInteropFee(string requestId, ulong feeAmount, int blockHeight, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                InteropConversionRequestFee interopConversionRequestFee = GetOrCreateInteropConversionRequestFeeLocked(requestId, blockHeight);

                if (!HasFeeProposalBeenConcluded(interopConversionRequestFee))
                {
                    // If the request id has no proposals, add it.
                    if (!this.feeProposalsByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> proposals))
                    {
                        // Add this pubkey's proposal.
                        this.logger.Debug($"Request doesn't exist, adding proposal fee of {feeAmount} for conversion request id '{requestId}' from {pubKey}.");
                        this.feeProposalsByRequestId.Add(requestId, new List<InterOpFeeToMultisig>() { new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount } });
                    }
                    else
                    {
                        if (!proposals.Any(p => p.PubKey == pubKey.ToHex()))
                        {
                            proposals.Add(new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount });
                            this.logger.Debug($"Request exists, adding proposal fee of {feeAmount} for conversion request id '{requestId}' from {pubKey}.");
                        }
                    }
                }
            }

            // TODO Rethink this.
            Task.Delay(TimeSpan.FromMilliseconds(500)).GetAwaiter().GetResult();

            // Broadcast/ask for this request from other nodes as well
            string signature = this.federationManager.CurrentFederationKey.SignMessage(requestId + feeAmount);
            this.federatedPegBroadcaster.BroadcastAsync(new FeeProposalPayload(requestId, feeAmount, blockHeight, signature)).GetAwaiter().GetResult();
        }

        private void AgreeOnInteropFeeForConversionRequest(InteropConversionRequestFee interopConversionRequestFee)
        {
            lock (this.lockObject)
            {
                if (!this.feeProposalsByRequestId.TryGetValue(interopConversionRequestFee.RequestId, out List<InterOpFeeToMultisig> proposals))
                {
                    this.logger.Error($"Fee proposal for request id '{interopConversionRequestFee.RequestId}' does not exist.");
                    return;
                }

                ulong candidateFee = (ulong)proposals.Select(s => Convert.ToInt64(s.FeeAmount)).Average();

                var interOpFeeToMultisig = new InterOpFeeToMultisig() { BlockHeight = interopConversionRequestFee.BlockHeight, PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(), FeeAmount = candidateFee };

                // If the request id doesn't exist yet, create a fee vote and broadcast it.
                if (!this.agreedFeeVotesByRequestId.TryGetValue(interopConversionRequestFee.RequestId, out List<InterOpFeeToMultisig> votes))
                {
                    this.logger.Debug($"No nodes has voted on conversion request id '{interopConversionRequestFee.RequestId}' with a fee amount of {candidateFee}.");
                    this.agreedFeeVotesByRequestId.Add(interopConversionRequestFee.RequestId, new List<InterOpFeeToMultisig>() { interOpFeeToMultisig });
                }
                else
                {
                    // Add this node's vote if its missing and has not yet concluded.
                    if (!HasFeeVoteBeenConcluded(interopConversionRequestFee.RequestId) && !votes.Any(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex()))
                    {
                        this.logger.Debug($"Adding fee vote for conversion request id '{interopConversionRequestFee.RequestId}' for amount {candidateFee}.");
                        votes.Add(interOpFeeToMultisig);
                    }
                }
            }

            this.agreedFeeVotesByRequestId.TryGetValue(interopConversionRequestFee.RequestId, out List<InterOpFeeToMultisig> processedVotes);

            this.logger.Debug($"{processedVotes.Count} node(s) has voted on a fee for conversion request id '{interopConversionRequestFee.RequestId}'.");

            if (HasFeeVoteBeenConcluded(interopConversionRequestFee.RequestId))
            {
                // Update the state and amount and save it.
                interopConversionRequestFee.Amount = (ulong)processedVotes.Select(s => Convert.ToInt64(s.FeeAmount)).Average();
                interopConversionRequestFee.State = InteropFeeState.AgreeanceConcluded;
                this.interopRequestKeyValueStore.SaveValueJson(interopConversionRequestFee.RequestId, interopConversionRequestFee);

                this.logger.Debug($"Voting on fee for request id '{interopConversionRequestFee.RequestId}' has concluded, amount: {interopConversionRequestFee.Amount}");
            }
            else
            {
                // If the fee vote is not concluded, broadcast again.
                InterOpFeeToMultisig myVote = processedVotes.First(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex());
                string signature = this.federationManager.CurrentFederationKey.SignMessage(interopConversionRequestFee.RequestId + myVote.FeeAmount);

                this.federatedPegBroadcaster.BroadcastAsync(new FeeAgreePayload(interopConversionRequestFee.RequestId, myVote.FeeAmount, interopConversionRequestFee.BlockHeight, signature)).GetAwaiter().GetResult();
            }
        }

        /// <inheritdoc/>
        public void MultiSigMemberAgreedOnInteropFee(string requestId, ulong feeAmount, int blockHeight, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                InteropConversionRequestFee interopConversionRequestFee = GetOrCreateInteropConversionRequestFeeLocked(requestId, blockHeight);

                if (!HasFeeVoteBeenConcluded(requestId))
                {
                    // If the request id has no votes, add it.
                    if (!this.agreedFeeVotesByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> votes))
                    {
                        if (!this.feeProposalsByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> proposals))
                        {
                            this.logger.Error($"Fee proposal for request id '{requestId}' does not exist.");
                            return;
                        }

                        // Add this pubkey's vote.
                        this.logger.Debug($"Fee vote doesn't exist, adding vote of {feeAmount} for conversion request id '{requestId}' from {pubKey}.");
                        this.agreedFeeVotesByRequestId.Add(requestId, new List<InterOpFeeToMultisig>() { new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount } });
                    }
                    else
                    {
                        if (!votes.Any(p => p.PubKey == pubKey.ToHex()))
                        {
                            votes.Add(new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount });
                            this.logger.Debug($"Request exists, adding fee vote of {feeAmount} for conversion request id '{requestId}' from {pubKey}.");
                        }
                    }
                }
            }

            // TODO Rethink this.
            Task.Delay(TimeSpan.FromMilliseconds(500)).GetAwaiter().GetResult();

            // Broadcast/ask for this vote from other nodes as well
            string signature = this.federationManager.CurrentFederationKey.SignMessage(requestId + feeAmount);
            this.federatedPegBroadcaster.BroadcastAsync(new FeeAgreePayload(requestId, feeAmount, blockHeight, signature)).GetAwaiter().GetResult();

        }

        private bool HasFeeProposalBeenConcluded(InteropConversionRequestFee interopConversionRequestFee)
        {
            if (this.feeProposalsByRequestId.TryGetValue(interopConversionRequestFee.RequestId, out List<InterOpFeeToMultisig> proposals))
                return proposals.Count >= this.quorum;

            return false;
        }

        private bool HasFeeVoteBeenConcluded(string requestId)
        {
            if (this.agreedFeeVotesByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> votes))
                return votes.Count >= this.quorum;

            return false;
        }

        /// <inheritdoc/>
        public void AddVote(string requestId, BigInteger transactionId, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                if (!this.receivedVotes.TryGetValue(requestId, out HashSet<PubKey> voted))
                    voted = new HashSet<PubKey>();

                // Ignore the vote if the pubkey has already submitted a vote.
                if (voted.Contains(pubKey))
                    return;

                this.logger.Info("Pubkey {0} adding vote for request {1}, transactionId {2}.", pubKey.ToHex(), requestId, transactionId);

                voted.Add(pubKey);

                if (!this.activeVotes.TryGetValue(requestId, out Dictionary<BigInteger, int> transactionIdVotes))
                    transactionIdVotes = new Dictionary<BigInteger, int>();

                if (!transactionIdVotes.ContainsKey(transactionId))
                    transactionIdVotes[transactionId] = 1;
                else
                    transactionIdVotes[transactionId]++;

                this.activeVotes[requestId] = transactionIdVotes;
                this.receivedVotes[requestId] = voted;
            }
        }

        /// <inheritdoc/>
        public BigInteger GetAgreedTransactionId(string requestId, int quorum)
        {
            lock (this.lockObject)
            {
                if (!this.activeVotes.ContainsKey(requestId))
                    return BigInteger.MinusOne;

                BigInteger highestVoted = BigInteger.MinusOne;
                int voteCount = 0;
                foreach (KeyValuePair<BigInteger, int> vote in this.activeVotes[requestId])
                {
                    if (vote.Value > voteCount && vote.Value >= quorum)
                    {
                        highestVoted = vote.Key;
                        voteCount = vote.Value;
                    }
                }

                return highestVoted;
            }
        }

        /// <inheritdoc/>
        public BigInteger GetCandidateTransactionId(string requestId)
        {
            lock (this.lockObject)
            {
                if (!this.activeVotes.ContainsKey(requestId))
                    return BigInteger.MinusOne;

                BigInteger highestVoted = BigInteger.MinusOne;
                int voteCount = 0;
                foreach (KeyValuePair<BigInteger, int> vote in this.activeVotes[requestId])
                {
                    if (vote.Value > voteCount)
                    {
                        highestVoted = vote.Key;
                        voteCount = vote.Value;
                    }
                }

                return highestVoted;
            }
        }

        /// <inheritdoc/>
        public bool CheckIfVoted(string requestId, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                if (!this.receivedVotes.ContainsKey(requestId))
                    return false;

                if (!this.receivedVotes[requestId].Contains(pubKey))
                    return false;

                return true;
            }
        }

        /// <inheritdoc/>
        public void RemoveTransaction(string requestId)
        {
            lock (this.lockObject)
            {
                this.activeVotes.Remove(requestId);
                this.receivedVotes.Remove(requestId);
            }
        }

        /// <inheritdoc/>
        public Dictionary<string, HashSet<PubKey>> GetStatus()
        {
            lock (this.lockObject)
            {
                return this.receivedVotes;
            }
        }

        public void RegisterQuorumSize(int quorum)
        {
            this.quorum = quorum;
        }

        public int GetQuorum()
        {
            return this.quorum;
        }

        private void AddComponentStats(StringBuilder benchLog)
        {
            benchLog.AppendLine(">> Interop Coordination Manager");
            benchLog.AppendLine();
            benchLog.AppendLine(">> Fee Proposals (last 5):");

            foreach (KeyValuePair<string, List<InterOpFeeToMultisig>> proposal in this.feeProposalsByRequestId.Take(5))
            {
                IEnumerable<long> values = proposal.Value.Select(s => Convert.ToInt64(s.FeeAmount));

                var state = proposal.Value.Count >= this.quorum ? "Concluded" : "In Progress";
                benchLog.AppendLine($"Height: {proposal.Value.First().BlockHeight}  Id: {proposal.Key} Proposals: {proposal.Value.Count} Fee (Avg): {new Money((long)values.Average())} State: {state}");
            }

            benchLog.AppendLine();
            benchLog.AppendLine(">> Fee Votes (last 5):");

            foreach (KeyValuePair<string, List<InterOpFeeToMultisig>> vote in this.agreedFeeVotesByRequestId.Take(5))
            {
                IEnumerable<long> values = vote.Value.Select(s => Convert.ToInt64(s.FeeAmount));

                var state = vote.Value.Count >= this.quorum ? "Concluded" : "In Progress";
                benchLog.AppendLine($"Height: {vote.Value.First().BlockHeight} Id: {vote.Key} Votes: {vote.Value.Count} Fee (Avg): {new Money((long)values.Average())} State: {state}");
            }

            benchLog.AppendLine();
        }
    }

    public sealed class InteropConversionRequestFee
    {
        [JsonProperty(PropertyName = "requestid")]
        public string RequestId { get; set; }

        [JsonProperty(PropertyName = "amount")]
        public ulong Amount { get; set; }

        [JsonProperty(PropertyName = "height")]
        public int BlockHeight { get; set; }

        [JsonProperty(PropertyName = "state")]
        public InteropFeeState State { get; set; }
    }

    public sealed class InterOpFeeToMultisig
    {
        [JsonProperty(PropertyName = "height")]
        public int BlockHeight { get; set; }

        [JsonProperty(PropertyName = "pubkey")]
        public string PubKey { get; set; }

        [JsonProperty(PropertyName = "fee")]
        public ulong FeeAmount { get; set; }
    }

    public enum InteropFeeState
    {
        ProposalInProgress,
        ProposalConcluded,
        AgreeanceInProgress,
        AgreeanceConcluded,
    }
}
