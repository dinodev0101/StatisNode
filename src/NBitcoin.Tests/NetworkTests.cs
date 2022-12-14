using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NBitcoin.BouncyCastle.Math;
using NBitcoin.DataEncoders;
using NBitcoin.Networks;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Networks.Deployments;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace NBitcoin.Tests
{
    public class NetworkTests
    {
        private readonly Network networkMain;
        private readonly Network straxMain;
        private readonly Network straxTest;
        private readonly Network straxRegTest;

        public NetworkTests()
        {
            this.networkMain = KnownNetworks.Main;
            this.straxMain = KnownNetworks.StraxMain;
            this.straxTest = KnownNetworks.StraxTest;
            this.straxRegTest = KnownNetworks.StraxRegTest;
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanGetNetworkFromName()
        {
            Network bitcoinMain = KnownNetworks.Main;
            Network bitcoinTestnet = KnownNetworks.TestNet;
            Network bitcoinRegtest = KnownNetworks.RegTest;
            Assert.Equal(NetworkRegistration.GetNetwork("main"), bitcoinMain);
            Assert.Equal(NetworkRegistration.GetNetwork("mainnet"), bitcoinMain);
            Assert.Equal(NetworkRegistration.GetNetwork("MainNet"), bitcoinMain);
            Assert.Equal(NetworkRegistration.GetNetwork("test"), bitcoinTestnet);
            Assert.Equal(NetworkRegistration.GetNetwork("testnet"), bitcoinTestnet);
            Assert.Equal(NetworkRegistration.GetNetwork("regtest"), bitcoinRegtest);
            Assert.Equal(NetworkRegistration.GetNetwork("reg"), bitcoinRegtest);
            Assert.Equal(NetworkRegistration.GetNetwork("straxmain"), this.straxMain);
            Assert.Equal(NetworkRegistration.GetNetwork("StraxMain"), this.straxMain);
            Assert.Equal(NetworkRegistration.GetNetwork("StraxTest"), this.straxTest);
            Assert.Equal(NetworkRegistration.GetNetwork("straxtest"), this.straxTest);
            Assert.Equal(NetworkRegistration.GetNetwork("StraxRegTest"), this.straxRegTest);
            Assert.Equal(NetworkRegistration.GetNetwork("straxregtest"), this.straxRegTest);
            Assert.Null(NetworkRegistration.GetNetwork("invalid"));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void RegisterNetworkTwiceReturnsSameNetwork()
        {
            Network main = KnownNetworks.Main;
            Network reregistered = NetworkRegistration.Register(main);
            Assert.Equal(main, reregistered);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void ReadMagicByteWithFirstByteDuplicated()
        {
            List<byte> bytes = this.networkMain.MagicBytes.ToList();
            bytes.Insert(0, bytes.First());

            using (var memStream = new MemoryStream(bytes.ToArray()))
            {
                bool found = this.networkMain.ReadMagic(memStream, new CancellationToken());
                Assert.True(found);
            }
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void BitcoinMainnetIsInitializedCorrectly()
        {
            Assert.Equal(17, this.networkMain.Checkpoints.Count);
            Assert.Equal(6, this.networkMain.DNSSeeds.Count);
            Assert.Equal(512, this.networkMain.SeedNodes.Count);

            Assert.Equal(NetworkRegistration.GetNetwork("main"), this.networkMain);
            Assert.Equal(NetworkRegistration.GetNetwork("mainnet"), this.networkMain);

            Assert.Equal("Main", this.networkMain.Name);
            Assert.Equal(BitcoinMain.BitcoinRootFolderName, this.networkMain.RootFolderName);
            Assert.Equal(BitcoinMain.BitcoinDefaultConfigFilename, this.networkMain.DefaultConfigFilename);
            Assert.Equal(0xD9B4BEF9, this.networkMain.Magic);
            Assert.Equal(8333, this.networkMain.DefaultPort);
            Assert.Equal(8332, this.networkMain.DefaultRPCPort);
            Assert.Equal(BitcoinMain.BitcoinMaxTimeOffsetSeconds, this.networkMain.MaxTimeOffsetSeconds);
            Assert.Equal(BitcoinMain.BitcoinDefaultMaxTipAgeInSeconds, this.networkMain.MaxTipAge);
            Assert.Equal(1000, this.networkMain.MinTxFee);
            Assert.Equal(20000, this.networkMain.FallbackFee);
            Assert.Equal(1000, this.networkMain.MinRelayTxFee);
            Assert.Equal("BTC", this.networkMain.CoinTicker);

            Assert.Equal(2, this.networkMain.Bech32Encoders.Length);
            Assert.Equal(new Bech32Encoder("bc").ToString(), this.networkMain.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS].ToString());
            Assert.Equal(new Bech32Encoder("bc").ToString(), this.networkMain.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS].ToString());

            Assert.Equal(12, this.networkMain.Base58Prefixes.Length);
            Assert.Equal(new byte[] { 0 }, this.networkMain.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS]);
            Assert.Equal(new byte[] { (5) }, this.networkMain.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS]);
            Assert.Equal(new byte[] { (128) }, this.networkMain.Base58Prefixes[(int)Base58Type.SECRET_KEY]);
            Assert.Equal(new byte[] { 0x01, 0x42 }, this.networkMain.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC]);
            Assert.Equal(new byte[] { 0x01, 0x43 }, this.networkMain.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC]);
            Assert.Equal(new byte[] { (0x04), (0x88), (0xB2), (0x1E) }, this.networkMain.Base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY]);
            Assert.Equal(new byte[] { (0x04), (0x88), (0xAD), (0xE4) }, this.networkMain.Base58Prefixes[(int)Base58Type.EXT_SECRET_KEY]);
            Assert.Equal(new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 }, this.networkMain.Base58Prefixes[(int)Base58Type.PASSPHRASE_CODE]);
            Assert.Equal(new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A }, this.networkMain.Base58Prefixes[(int)Base58Type.CONFIRMATION_CODE]);
            Assert.Equal(new byte[] { 0x2a }, this.networkMain.Base58Prefixes[(int)Base58Type.STEALTH_ADDRESS]);
            Assert.Equal(new byte[] { 23 }, this.networkMain.Base58Prefixes[(int)Base58Type.ASSET_ID]);
            Assert.Equal(new byte[] { 0x13 }, this.networkMain.Base58Prefixes[(int)Base58Type.COLORED_ADDRESS]);

            Assert.Equal(210000, this.networkMain.Consensus.SubsidyHalvingInterval);
            Assert.Equal(750, this.networkMain.Consensus.MajorityEnforceBlockUpgrade);
            Assert.Equal(950, this.networkMain.Consensus.MajorityRejectBlockOutdated);
            Assert.Equal(1000, this.networkMain.Consensus.MajorityWindow);
            Assert.Equal(227931, this.networkMain.Consensus.BuriedDeployments[BuriedDeployments.BIP34]);
            Assert.Equal(388381, this.networkMain.Consensus.BuriedDeployments[BuriedDeployments.BIP65]);
            Assert.Equal(363725, this.networkMain.Consensus.BuriedDeployments[BuriedDeployments.BIP66]);
            Assert.Equal(new uint256("0x000000000000024b89b42a942fe0d9fea3bb44ab7bd1b19115dd6a759c0808b8"), this.networkMain.Consensus.BIP34Hash);
            Assert.Equal(new Target(new uint256("00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff")), this.networkMain.Consensus.PowLimit);
            Assert.Equal(new uint256("0x0000000000000000000000000000000000000000002cb971dd56d1c583c20f90"), this.networkMain.Consensus.MinimumChainWork);
            Assert.Equal(TimeSpan.FromSeconds(14 * 24 * 60 * 60), this.networkMain.Consensus.PowTargetTimespan);
            Assert.Equal(TimeSpan.FromSeconds(10 * 60), this.networkMain.Consensus.TargetSpacing);
            Assert.False(this.networkMain.Consensus.PowAllowMinDifficultyBlocks);
            Assert.False(this.networkMain.Consensus.PowNoRetargeting);
            Assert.Equal(2016, this.networkMain.Consensus.MinerConfirmationWindow);
            Assert.Equal(28, this.networkMain.Consensus.BIP9Deployments[BitcoinBIP9Deployments.TestDummy].Bit);
            Assert.Equal(Utils.UnixTimeToDateTime(1199145601), this.networkMain.Consensus.BIP9Deployments[BitcoinBIP9Deployments.TestDummy].StartTime);
            Assert.Equal(Utils.UnixTimeToDateTime(1230767999), this.networkMain.Consensus.BIP9Deployments[BitcoinBIP9Deployments.TestDummy].Timeout);
            Assert.Equal(0, this.networkMain.Consensus.BIP9Deployments[BitcoinBIP9Deployments.CSV].Bit);
            Assert.Equal(Utils.UnixTimeToDateTime(1462060800), this.networkMain.Consensus.BIP9Deployments[BitcoinBIP9Deployments.CSV].StartTime);
            Assert.Equal(Utils.UnixTimeToDateTime(1493596800), this.networkMain.Consensus.BIP9Deployments[BitcoinBIP9Deployments.CSV].Timeout);
            Assert.Equal(1, this.networkMain.Consensus.BIP9Deployments[BitcoinBIP9Deployments.Segwit].Bit);
            Assert.Equal(Utils.UnixTimeToDateTime(1479168000), this.networkMain.Consensus.BIP9Deployments[BitcoinBIP9Deployments.Segwit].StartTime);
            Assert.Equal(Utils.UnixTimeToDateTime(1510704000), this.networkMain.Consensus.BIP9Deployments[BitcoinBIP9Deployments.Segwit].Timeout);
            Assert.Equal(0, this.networkMain.Consensus.CoinType);
            Assert.False(this.networkMain.Consensus.IsProofOfStake);
            Assert.Equal(new uint256("0x0000000000000000000f1c54590ee18d15ec70e68c8cd4cfbadb1b4f11697eee"), this.networkMain.Consensus.DefaultAssumeValid);
            Assert.Equal(100, this.networkMain.Consensus.CoinbaseMaturity);
            Assert.Equal(0, this.networkMain.Consensus.PremineReward);
            Assert.Equal(0, this.networkMain.Consensus.PremineHeight);
            Assert.Equal(Money.Coins(50), this.networkMain.Consensus.ProofOfWorkReward);
            Assert.Equal(Money.Zero, this.networkMain.Consensus.ProofOfStakeReward);
            Assert.Equal((uint)0, this.networkMain.Consensus.MaxReorgLength);
            Assert.Equal(21000000 * Money.COIN, this.networkMain.Consensus.MaxMoney);

            Block genesis = this.networkMain.GetGenesis();
            Assert.Equal(uint256.Parse("0x000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f"), genesis.GetHash());
            Assert.Equal(uint256.Parse("0x4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b"), genesis.Header.HashMerkleRoot);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void BitcoinTestnetIsInitializedCorrectly()
        {
            Network network = KnownNetworks.TestNet;

            Assert.Equal(13, network.Checkpoints.Count);
            Assert.Equal(3, network.DNSSeeds.Count);
            Assert.Empty(network.SeedNodes);

            Assert.Equal("TestNet", network.Name);
            Assert.Equal(BitcoinMain.BitcoinRootFolderName, network.RootFolderName);
            Assert.Equal(BitcoinMain.BitcoinDefaultConfigFilename, network.DefaultConfigFilename);
            Assert.Equal(0x0709110B.ToString(), network.Magic.ToString());
            Assert.Equal(18333, network.DefaultPort);
            Assert.Equal(18332, network.DefaultRPCPort);
            Assert.Equal(BitcoinMain.BitcoinMaxTimeOffsetSeconds, network.MaxTimeOffsetSeconds);
            Assert.Equal(BitcoinMain.BitcoinDefaultMaxTipAgeInSeconds, network.MaxTipAge);
            Assert.Equal(1000, network.MinTxFee);
            Assert.Equal(20000, network.FallbackFee);
            Assert.Equal(1000, network.MinRelayTxFee);
            Assert.Equal("TBTC", network.CoinTicker);

            Assert.Equal(2, network.Bech32Encoders.Length);
            Assert.Equal(new Bech32Encoder("tb").ToString(), network.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS].ToString());
            Assert.Equal(new Bech32Encoder("tb").ToString(), network.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS].ToString());

            Assert.Equal(12, network.Base58Prefixes.Length);
            Assert.Equal(new byte[] { 111 }, network.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS]);
            Assert.Equal(new byte[] { (196) }, network.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS]);
            Assert.Equal(new byte[] { (239) }, network.Base58Prefixes[(int)Base58Type.SECRET_KEY]);
            Assert.Equal(new byte[] { 0x01, 0x42 }, network.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC]);
            Assert.Equal(new byte[] { 0x01, 0x43 }, network.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC]);
            Assert.Equal(new byte[] { (0x04), (0x35), (0x87), (0xCF) }, network.Base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY]);
            Assert.Equal(new byte[] { (0x04), (0x35), (0x83), (0x94) }, network.Base58Prefixes[(int)Base58Type.EXT_SECRET_KEY]);
            Assert.Equal(new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 }, network.Base58Prefixes[(int)Base58Type.PASSPHRASE_CODE]);
            Assert.Equal(new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A }, network.Base58Prefixes[(int)Base58Type.CONFIRMATION_CODE]);
            Assert.Equal(new byte[] { 0x2b }, network.Base58Prefixes[(int)Base58Type.STEALTH_ADDRESS]);
            Assert.Equal(new byte[] { 115 }, network.Base58Prefixes[(int)Base58Type.ASSET_ID]);
            Assert.Equal(new byte[] { 0x13 }, network.Base58Prefixes[(int)Base58Type.COLORED_ADDRESS]);

            Assert.Equal(210000, network.Consensus.SubsidyHalvingInterval);
            Assert.Equal(51, network.Consensus.MajorityEnforceBlockUpgrade);
            Assert.Equal(75, network.Consensus.MajorityRejectBlockOutdated);
            Assert.Equal(100, network.Consensus.MajorityWindow);
            Assert.Equal(21111, network.Consensus.BuriedDeployments[BuriedDeployments.BIP34]);
            Assert.Equal(581885, network.Consensus.BuriedDeployments[BuriedDeployments.BIP65]);
            Assert.Equal(330776, network.Consensus.BuriedDeployments[BuriedDeployments.BIP66]);
            Assert.Equal(new uint256("0x0000000023b3a96d3484e5abb3755c413e7d41500f8e2a5c3f0dd01299cd8ef8"), network.Consensus.BIP34Hash);
            Assert.Equal(new Target(new uint256("00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff")), network.Consensus.PowLimit);
            Assert.Equal(new uint256("0x0000000000000000000000000000000000000000000000198b4def2baa9338d6"), network.Consensus.MinimumChainWork);
            Assert.Equal(TimeSpan.FromSeconds(14 * 24 * 60 * 60), network.Consensus.PowTargetTimespan);
            Assert.Equal(TimeSpan.FromSeconds(10 * 60), network.Consensus.TargetSpacing);
            Assert.True(network.Consensus.PowAllowMinDifficultyBlocks);
            Assert.False(network.Consensus.PowNoRetargeting);
            Assert.Equal(2016, network.Consensus.MinerConfirmationWindow);
            Assert.Equal(28, network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.TestDummy].Bit);
            Assert.Equal(Utils.UnixTimeToDateTime(1199145601), network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.TestDummy].StartTime);
            Assert.Equal(Utils.UnixTimeToDateTime(1230767999), network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.TestDummy].Timeout);
            Assert.Equal(0, network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.CSV].Bit);
            Assert.Equal(Utils.UnixTimeToDateTime(1456790400), network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.CSV].StartTime);
            Assert.Equal(Utils.UnixTimeToDateTime(1493596800), network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.CSV].Timeout);
            Assert.Equal(1, network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.Segwit].Bit);
            Assert.Equal(Utils.UnixTimeToDateTime(1462060800), network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.Segwit].StartTime);
            Assert.Equal(Utils.UnixTimeToDateTime(1493596800), network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.Segwit].Timeout);
            Assert.Equal(1, network.Consensus.CoinType);
            Assert.False(network.Consensus.IsProofOfStake);
            Assert.Equal(new uint256("0x0000000000000037a8cd3e06cd5edbfe9dd1dbcc5dacab279376ef7cfc2b4c75"), network.Consensus.DefaultAssumeValid);
            Assert.Equal(100, network.Consensus.CoinbaseMaturity);
            Assert.Equal(0, network.Consensus.PremineReward);
            Assert.Equal(0, network.Consensus.PremineHeight);
            Assert.Equal(Money.Coins(50), network.Consensus.ProofOfWorkReward);
            Assert.Equal(Money.Zero, network.Consensus.ProofOfStakeReward);
            Assert.Equal((uint)0, network.Consensus.MaxReorgLength);
            Assert.Equal(21000000 * Money.COIN, network.Consensus.MaxMoney);

            Block genesis = network.GetGenesis();
            Assert.Equal(uint256.Parse("0x000000000933ea01ad0ee984209779baaec3ced90fa3f408719526f8d77f4943"), genesis.GetHash());
            Assert.Equal(uint256.Parse("0x4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b"), genesis.Header.HashMerkleRoot);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void BitcoinRegTestIsInitializedCorrectly()
        {
            Network network = KnownNetworks.RegTest;

            Assert.Empty(network.Checkpoints);
            Assert.Empty(network.DNSSeeds);
            Assert.Empty(network.SeedNodes);

            Assert.Equal("RegTest", network.Name);
            Assert.Equal(BitcoinMain.BitcoinRootFolderName, network.RootFolderName);
            Assert.Equal(BitcoinMain.BitcoinDefaultConfigFilename, network.DefaultConfigFilename);
            Assert.Equal(0xDAB5BFFA, network.Magic);
            Assert.Equal(18444, network.DefaultPort);
            Assert.Equal(18332, network.DefaultRPCPort);
            Assert.Equal(BitcoinMain.BitcoinMaxTimeOffsetSeconds, network.MaxTimeOffsetSeconds);
            Assert.Equal(BitcoinMain.BitcoinDefaultMaxTipAgeInSeconds, network.MaxTipAge);
            Assert.Equal(1000, network.MinTxFee);
            Assert.Equal(20000, network.FallbackFee);
            Assert.Equal(1000, network.MinRelayTxFee);
            Assert.Equal("TBTC", network.CoinTicker);

            Assert.Equal(2, network.Bech32Encoders.Length);
            Assert.Equal(new Bech32Encoder("tb").ToString(), network.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS].ToString());
            Assert.Equal(new Bech32Encoder("tb").ToString(), network.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS].ToString());

            Assert.Equal(12, network.Base58Prefixes.Length);
            Assert.Equal(new byte[] { 111 }, network.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS]);
            Assert.Equal(new byte[] { (196) }, network.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS]);
            Assert.Equal(new byte[] { (239) }, network.Base58Prefixes[(int)Base58Type.SECRET_KEY]);
            Assert.Equal(new byte[] { 0x01, 0x42 }, network.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC]);
            Assert.Equal(new byte[] { 0x01, 0x43 }, network.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC]);
            Assert.Equal(new byte[] { (0x04), (0x35), (0x87), (0xCF) }, network.Base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY]);
            Assert.Equal(new byte[] { (0x04), (0x35), (0x83), (0x94) }, network.Base58Prefixes[(int)Base58Type.EXT_SECRET_KEY]);
            Assert.Equal(new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 }, network.Base58Prefixes[(int)Base58Type.PASSPHRASE_CODE]);
            Assert.Equal(new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A }, network.Base58Prefixes[(int)Base58Type.CONFIRMATION_CODE]);
            Assert.Equal(new byte[] { 0x2b }, network.Base58Prefixes[(int)Base58Type.STEALTH_ADDRESS]);
            Assert.Equal(new byte[] { 115 }, network.Base58Prefixes[(int)Base58Type.ASSET_ID]);
            Assert.Equal(new byte[] { 0x13 }, network.Base58Prefixes[(int)Base58Type.COLORED_ADDRESS]);

            Assert.Equal(150, network.Consensus.SubsidyHalvingInterval);
            Assert.Equal(750, network.Consensus.MajorityEnforceBlockUpgrade);
            Assert.Equal(950, network.Consensus.MajorityRejectBlockOutdated);
            Assert.Equal(1000, network.Consensus.MajorityWindow);
            Assert.Equal(100000000, network.Consensus.BuriedDeployments[BuriedDeployments.BIP34]);
            Assert.Equal(100000000, network.Consensus.BuriedDeployments[BuriedDeployments.BIP65]);
            Assert.Equal(100000000, network.Consensus.BuriedDeployments[BuriedDeployments.BIP66]);
            Assert.Equal(new uint256(), network.Consensus.BIP34Hash);
            Assert.Equal(new Target(new uint256("7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")), network.Consensus.PowLimit);
            Assert.Equal(uint256.Zero, network.Consensus.MinimumChainWork);
            Assert.Equal(TimeSpan.FromSeconds(14 * 24 * 60 * 60), network.Consensus.PowTargetTimespan);
            Assert.Equal(TimeSpan.FromSeconds(10 * 60), network.Consensus.TargetSpacing);
            Assert.True(network.Consensus.PowAllowMinDifficultyBlocks);
            Assert.True(network.Consensus.PowNoRetargeting);
            Assert.Equal(144, network.Consensus.MinerConfirmationWindow);
            Assert.Equal(28, network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.TestDummy].Bit);
            Assert.Equal(Utils.UnixTimeToDateTime(0), network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.TestDummy].StartTime);
            Assert.Equal(Utils.UnixTimeToDateTime(999999999), network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.TestDummy].Timeout);
            Assert.Equal(0, network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.CSV].Bit);
            Assert.Equal(Utils.UnixTimeToDateTime(0), network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.CSV].StartTime);
            Assert.Equal(Utils.UnixTimeToDateTime(999999999), network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.CSV].Timeout);
            Assert.Equal(1, network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.Segwit].Bit);
            Assert.Equal(Utils.UnixTimeToDateTime(BIP9DeploymentsParameters.AlwaysActive), network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.Segwit].StartTime);
            Assert.Equal(Utils.UnixTimeToDateTime(999999999), network.Consensus.BIP9Deployments[BitcoinBIP9Deployments.Segwit].Timeout);
            Assert.Equal(0, network.Consensus.CoinType);
            Assert.False(network.Consensus.IsProofOfStake);
            Assert.Null(network.Consensus.DefaultAssumeValid);
            Assert.Equal(100, network.Consensus.CoinbaseMaturity);
            Assert.Equal(0, network.Consensus.PremineReward);
            Assert.Equal(0, network.Consensus.PremineHeight);
            Assert.Equal(Money.Coins(50), network.Consensus.ProofOfWorkReward);
            Assert.Equal(Money.Zero, network.Consensus.ProofOfStakeReward);
            Assert.Equal((uint)0, network.Consensus.MaxReorgLength);
            Assert.Equal(21000000 * Money.COIN, network.Consensus.MaxMoney);

            Block genesis = network.GetGenesis();
            Assert.Equal(uint256.Parse("0x0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206"), genesis.GetHash());
            Assert.Equal(uint256.Parse("0x4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b"), genesis.Header.HashMerkleRoot);
        }

        [Fact]
        public void StraxMainIsInitializedCorrectly()
        {
            Network network = this.straxMain;

            Assert.Equal(2, network.DNSSeeds.Count);
            Assert.Empty(network.SeedNodes);

            Assert.Equal("StraxMain", network.Name);
            Assert.Equal(StraxNetwork.StraxRootFolderName, network.RootFolderName);
            Assert.Equal(StraxNetwork.StraxDefaultConfigFilename, network.DefaultConfigFilename);
            Assert.Equal(BitConverter.ToUInt32(Encoding.ASCII.GetBytes("StrX")).ToString(), network.Magic.ToString());
            Assert.Equal(17105, network.DefaultPort);
            Assert.Equal(17104, network.DefaultRPCPort);
            Assert.Equal(StraxNetwork.StratisMaxTimeOffsetSeconds, network.MaxTimeOffsetSeconds);
            Assert.Equal(StraxNetwork.StratisDefaultMaxTipAgeInSeconds, network.MaxTipAge);
            Assert.Equal(10000, network.MinTxFee);
            Assert.Equal(10000, network.FallbackFee);
            Assert.Equal(10000, network.MinRelayTxFee);
            Assert.Equal("STRAX", network.CoinTicker);

            Assert.Equal(2, network.Bech32Encoders.Length);
            Assert.Equal(new Bech32Encoder("strax").ToString(), network.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS].ToString());
            Assert.Equal(new Bech32Encoder("strax").ToString(), network.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS].ToString());

            Assert.Equal(12, network.Base58Prefixes.Length);
            Assert.Equal(new byte[] { (75) }, network.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS]);
            Assert.Equal(new byte[] { (140) }, network.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS]);
            Assert.Equal(new byte[] { (75 + 128) }, network.Base58Prefixes[(int)Base58Type.SECRET_KEY]);
            Assert.Equal(new byte[] { 0x01, 0x42 }, network.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC]);
            Assert.Equal(new byte[] { 0x01, 0x43 }, network.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC]);
            Assert.Equal(new byte[] { (0x04), (0x88), (0xB2), (0x1E) }, network.Base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY]);
            Assert.Equal(new byte[] { (0x04), (0x88), (0xAD), (0xE4) }, network.Base58Prefixes[(int)Base58Type.EXT_SECRET_KEY]);
            Assert.Equal(new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 }, network.Base58Prefixes[(int)Base58Type.PASSPHRASE_CODE]);
            Assert.Equal(new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A }, network.Base58Prefixes[(int)Base58Type.CONFIRMATION_CODE]);
            Assert.Equal(new byte[] { 0x2a }, network.Base58Prefixes[(int)Base58Type.STEALTH_ADDRESS]);
            Assert.Equal(new byte[] { 23 }, network.Base58Prefixes[(int)Base58Type.ASSET_ID]);
            Assert.Equal(new byte[] { 0x13 }, network.Base58Prefixes[(int)Base58Type.COLORED_ADDRESS]);

            Assert.Equal(210000, network.Consensus.SubsidyHalvingInterval);
            Assert.Equal(750, network.Consensus.MajorityEnforceBlockUpgrade);
            Assert.Equal(950, network.Consensus.MajorityRejectBlockOutdated);
            Assert.Equal(1000, network.Consensus.MajorityWindow);
            Assert.Equal(0, network.Consensus.BuriedDeployments[BuriedDeployments.BIP34]);
            Assert.Equal(0, network.Consensus.BuriedDeployments[BuriedDeployments.BIP65]);
            Assert.Equal(0, network.Consensus.BuriedDeployments[BuriedDeployments.BIP66]);
            Assert.Null(network.Consensus.BIP34Hash);
            Assert.Equal(new Target(new uint256("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")), network.Consensus.PowLimit);
            Assert.Null(network.Consensus.MinimumChainWork);
            Assert.Equal(TimeSpan.FromSeconds(14 * 24 * 60 * 60), network.Consensus.PowTargetTimespan);
            Assert.Equal(TimeSpan.FromSeconds(45), network.Consensus.TargetSpacing);
            Assert.False(network.Consensus.PowAllowMinDifficultyBlocks);
            Assert.False(network.Consensus.PowNoRetargeting);
            Assert.Equal(2016, network.Consensus.MinerConfirmationWindow);
            Assert.Equal(675, network.Consensus.LastPOWBlock);
            Assert.True(network.Consensus.IsProofOfStake);
            Assert.Equal(105105, network.Consensus.CoinType);
            Assert.Equal(new BigInteger(uint256.Parse("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)), network.Consensus.ProofOfStakeLimit);
            Assert.Equal(new BigInteger(uint256.Parse("000000000000ffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)), network.Consensus.ProofOfStakeLimitV2);
            Assert.Null(network.Consensus.DefaultAssumeValid);
            Assert.Equal(50, network.Consensus.CoinbaseMaturity);
            Assert.Equal(Money.Coins(124987850), network.Consensus.PremineReward);
            Assert.Equal(2, network.Consensus.PremineHeight);
            Assert.Equal(Money.Coins(18), network.Consensus.ProofOfWorkReward);
            Assert.Equal(Money.Coins(18), network.Consensus.ProofOfStakeReward);
            Assert.Equal((uint)500, network.Consensus.MaxReorgLength);
            Assert.Equal(long.MaxValue, network.Consensus.MaxMoney);

            Block genesis = network.GetGenesis();
            Assert.Equal(uint256.Parse("0xebe158d09325c470276619ebc5f7f87c98c0ed4b211c46a17a6457655811d082"), genesis.GetHash());
            Assert.Equal(uint256.Parse("0xdd91e99b7ca5eb97d9c41b867762d1f2db412ba4331efb61d138fce5d39b9084"), genesis.Header.HashMerkleRoot);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void StraxTestnetIsInitializedCorrectly()
        {
            Network network = this.straxTest;

            Assert.Single(network.SeedNodes);

            Assert.Equal("StraxTest", network.Name);
            Assert.Equal(StraxNetwork.StraxRootFolderName, network.RootFolderName);
            Assert.Equal(StraxNetwork.StraxDefaultConfigFilename, network.DefaultConfigFilename);
            Assert.Equal(BitConverter.ToUInt32(Encoding.ASCII.GetBytes("TtrX")).ToString(), network.Magic.ToString());
            Assert.Equal(27105, network.DefaultPort);
            Assert.Equal(27104, network.DefaultRPCPort);
            Assert.Equal(StraxNetwork.StratisMaxTimeOffsetSeconds, network.MaxTimeOffsetSeconds);
            Assert.Equal(StraxNetwork.StratisDefaultMaxTipAgeInSeconds, network.MaxTipAge);
            Assert.Equal(10000, network.MinTxFee);
            Assert.Equal(10000, network.FallbackFee);
            Assert.Equal(10000, network.MinRelayTxFee);
            Assert.Equal("TSTRAX", network.CoinTicker);

            Assert.Equal(2, network.Bech32Encoders.Length);
            Assert.Equal(new Bech32Encoder("tstrax").ToString(), network.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS].ToString());
            Assert.Equal(new Bech32Encoder("tstrax").ToString(), network.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS].ToString());

            Assert.Equal(12, network.Base58Prefixes.Length);
            Assert.Equal(new byte[] { (120) }, network.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS]);
            Assert.Equal(new byte[] { (127) }, network.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS]);
            Assert.Equal(new byte[] { (120 + 128) }, network.Base58Prefixes[(int)Base58Type.SECRET_KEY]);
            Assert.Equal(new byte[] { 0x01, 0x42 }, network.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC]);
            Assert.Equal(new byte[] { 0x01, 0x43 }, network.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC]);
            Assert.Equal(new byte[] { (0x04), (0x88), (0xB2), (0x1E) }, network.Base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY]);
            Assert.Equal(new byte[] { (0x04), (0x88), (0xAD), (0xE4) }, network.Base58Prefixes[(int)Base58Type.EXT_SECRET_KEY]);
            Assert.Equal(new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 }, network.Base58Prefixes[(int)Base58Type.PASSPHRASE_CODE]);
            Assert.Equal(new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A }, network.Base58Prefixes[(int)Base58Type.CONFIRMATION_CODE]);
            Assert.Equal(new byte[] { 0x2a }, network.Base58Prefixes[(int)Base58Type.STEALTH_ADDRESS]);
            Assert.Equal(new byte[] { 23 }, network.Base58Prefixes[(int)Base58Type.ASSET_ID]);
            Assert.Equal(new byte[] { 0x13 }, network.Base58Prefixes[(int)Base58Type.COLORED_ADDRESS]);

            Assert.Equal(210000, network.Consensus.SubsidyHalvingInterval);
            Assert.Equal(750, network.Consensus.MajorityEnforceBlockUpgrade);
            Assert.Equal(950, network.Consensus.MajorityRejectBlockOutdated);
            Assert.Equal(1000, network.Consensus.MajorityWindow);
            Assert.Equal(0, network.Consensus.BuriedDeployments[BuriedDeployments.BIP34]);
            Assert.Equal(0, network.Consensus.BuriedDeployments[BuriedDeployments.BIP65]);
            Assert.Equal(0, network.Consensus.BuriedDeployments[BuriedDeployments.BIP66]);
            Assert.Null(network.Consensus.BIP34Hash);
            Assert.Equal(new Target(new uint256("0000ffff00000000000000000000000000000000000000000000000000000000")), network.Consensus.PowLimit);
            Assert.Null(network.Consensus.MinimumChainWork);
            Assert.Equal(TimeSpan.FromSeconds(14 * 24 * 60 * 60), network.Consensus.PowTargetTimespan);
            Assert.Equal(TimeSpan.FromSeconds(45), network.Consensus.TargetSpacing);
            Assert.False(network.Consensus.PowAllowMinDifficultyBlocks);
            Assert.False(network.Consensus.PowNoRetargeting);
            Assert.Equal(2016, network.Consensus.MinerConfirmationWindow);
            Assert.Equal(12500, network.Consensus.LastPOWBlock);
            Assert.True(network.Consensus.IsProofOfStake);
            Assert.Equal(1, network.Consensus.CoinType);
            Assert.Equal(new BigInteger(uint256.Parse("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)), network.Consensus.ProofOfStakeLimit);
            Assert.Equal(new BigInteger(uint256.Parse("000000000000ffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)), network.Consensus.ProofOfStakeLimitV2);
            Assert.Null(network.Consensus.DefaultAssumeValid);
            Assert.Equal(50, network.Consensus.CoinbaseMaturity);
            Assert.Equal(Money.Coins(130000000), network.Consensus.PremineReward);
            Assert.Equal(2, network.Consensus.PremineHeight);
            Assert.Equal(Money.Coins(18), network.Consensus.ProofOfWorkReward);
            Assert.Equal(Money.Coins(18), network.Consensus.ProofOfStakeReward);
            Assert.Equal((uint)500, network.Consensus.MaxReorgLength);
            Assert.Equal(long.MaxValue, network.Consensus.MaxMoney);

            Block genesis = network.GetGenesis();
            Assert.Equal(uint256.Parse("0x0000db68ff9e74fbaf7654bab4fa702c237318428fa9186055c243ddde6354ca"), genesis.GetHash());
            Assert.Equal(uint256.Parse("0xfe6317d42149b091399e7f834ca32fd248f8f26f493c30a35d6eea692fe4fcad"), genesis.Header.HashMerkleRoot);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void StraxRegTestIsInitializedCorrectly()
        {
            Network network = this.straxRegTest;

            Assert.Empty(network.Checkpoints);
            Assert.Empty(network.DNSSeeds);
            Assert.Empty(network.SeedNodes);

            Assert.Equal("StraxRegTest", network.Name);
            Assert.Equal(StraxNetwork.StraxRootFolderName, network.RootFolderName);
            Assert.Equal(StraxNetwork.StraxDefaultConfigFilename, network.DefaultConfigFilename);
            Assert.Equal(BitConverter.ToUInt32(Encoding.ASCII.GetBytes("RtrX")), network.Magic);
            Assert.Equal(37105, network.DefaultPort);
            Assert.Equal(37104, network.DefaultRPCPort);
            Assert.Equal(StraxNetwork.StratisMaxTimeOffsetSeconds, network.MaxTimeOffsetSeconds);
            Assert.Equal(StraxNetwork.StratisDefaultMaxTipAgeInSeconds, network.MaxTipAge);
            Assert.Equal(10000, network.MinTxFee);
            Assert.Equal(10000, network.FallbackFee);
            Assert.Equal(10000, network.MinRelayTxFee);
            Assert.Equal("TSTRAX", network.CoinTicker);

            Assert.Equal(2, network.Bech32Encoders.Length);
            Assert.Equal(new Bech32Encoder("tstrax").ToString(), network.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS].ToString());
            Assert.Equal(new Bech32Encoder("tstrax").ToString(), network.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS].ToString());

            Assert.Equal(12, network.Base58Prefixes.Length);
            Assert.Equal(new byte[] { (120) }, network.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS]);
            Assert.Equal(new byte[] { (127) }, network.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS]);
            Assert.Equal(new byte[] { (120 + 128) }, network.Base58Prefixes[(int)Base58Type.SECRET_KEY]);
            Assert.Equal(new byte[] { 0x01, 0x42 }, network.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC]);
            Assert.Equal(new byte[] { 0x01, 0x43 }, network.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC]);
            Assert.Equal(new byte[] { (0x04), (0x88), (0xB2), (0x1E) }, network.Base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY]);
            Assert.Equal(new byte[] { (0x04), (0x88), (0xAD), (0xE4) }, network.Base58Prefixes[(int)Base58Type.EXT_SECRET_KEY]);
            Assert.Equal(new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 }, network.Base58Prefixes[(int)Base58Type.PASSPHRASE_CODE]);
            Assert.Equal(new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A }, network.Base58Prefixes[(int)Base58Type.CONFIRMATION_CODE]);
            Assert.Equal(new byte[] { 0x2a }, network.Base58Prefixes[(int)Base58Type.STEALTH_ADDRESS]);
            Assert.Equal(new byte[] { 23 }, network.Base58Prefixes[(int)Base58Type.ASSET_ID]);
            Assert.Equal(new byte[] { 0x13 }, network.Base58Prefixes[(int)Base58Type.COLORED_ADDRESS]);

            Assert.Equal(210000, network.Consensus.SubsidyHalvingInterval);
            Assert.Equal(750, network.Consensus.MajorityEnforceBlockUpgrade);
            Assert.Equal(950, network.Consensus.MajorityRejectBlockOutdated);
            Assert.Equal(1000, network.Consensus.MajorityWindow);
            Assert.Equal(0, network.Consensus.BuriedDeployments[BuriedDeployments.BIP34]);
            Assert.Equal(0, network.Consensus.BuriedDeployments[BuriedDeployments.BIP65]);
            Assert.Equal(0, network.Consensus.BuriedDeployments[BuriedDeployments.BIP66]);
            Assert.Null(network.Consensus.BIP34Hash);
            Assert.Equal(new Target(new uint256("7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")), network.Consensus.PowLimit);
            Assert.Null(network.Consensus.MinimumChainWork);
            Assert.Equal(TimeSpan.FromSeconds(14 * 24 * 60 * 60), network.Consensus.PowTargetTimespan);
            Assert.Equal(TimeSpan.FromSeconds(45), network.Consensus.TargetSpacing);
            Assert.True(network.Consensus.PowAllowMinDifficultyBlocks);
            Assert.True(network.Consensus.PowNoRetargeting);
            Assert.Equal(144, network.Consensus.MinerConfirmationWindow);
            Assert.Equal(12500, network.Consensus.LastPOWBlock);
            Assert.True(network.Consensus.IsProofOfStake);
            Assert.Equal(1, network.Consensus.CoinType);
            Assert.Equal(new BigInteger(uint256.Parse("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)), network.Consensus.ProofOfStakeLimit);
            Assert.Equal(new BigInteger(uint256.Parse("000000000000ffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)), network.Consensus.ProofOfStakeLimitV2);
            Assert.Null(network.Consensus.DefaultAssumeValid);
            Assert.Equal(10, network.Consensus.CoinbaseMaturity);
            Assert.Equal(Money.Coins(130000000), network.Consensus.PremineReward);
            Assert.Equal(2, network.Consensus.PremineHeight);
            Assert.Equal(Money.Coins(18), network.Consensus.ProofOfWorkReward);
            Assert.Equal(Money.Coins(18), network.Consensus.ProofOfStakeReward);
            Assert.Equal((uint)500, network.Consensus.MaxReorgLength);
            Assert.Equal(long.MaxValue, network.Consensus.MaxMoney);

            Block genesis = network.GetGenesis();
            Assert.Equal(uint256.Parse("0x77283cca51b83fe3bda9ce8966248613036b0dc55a707ce76ca7b79aaa9962e4"), genesis.GetHash());
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void MineGenesisBlockWithMissingParametersThrowsException()
        {
            Assert.Throws<ArgumentException>(() => Network.MineGenesisBlock(null, "some string", new Target(new uint256()), Money.Zero));
            Assert.Throws<ArgumentException>(() => Network.MineGenesisBlock(new ConsensusFactory(), "", new Target(new uint256()), Money.Zero));
            Assert.Throws<ArgumentException>(() => Network.MineGenesisBlock(new ConsensusFactory(), "some string", null, Money.Zero));
            Assert.Throws<ArgumentException>(() => Network.MineGenesisBlock(new ConsensusFactory(), "some string", new Target(new uint256()), null));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void MineGenesisBlockWithLongCoinbaseTextThrowsException()
        {
            string coinbaseText100Long = "1111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111";
            Assert.Throws<ArgumentException>(() => Network.MineGenesisBlock(new ConsensusFactory(), coinbaseText100Long, new Target(new uint256()), Money.Zero));
        }
    }
}
