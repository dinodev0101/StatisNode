Stratis (STRAX) Full Node 
===============

Bitcoin Implementation in C#
----------------------------

Stratis is an implementation of the Bitcoin protocol in C# on the [.NET Core](https://dotnet.github.io/) platform.  
The node can run on the Bitcoin and Stratis networks.  
Stratis Bitcoin is based on the [NBitcoin](https://github.com/MetacoSA/NBitcoin) project.  

For Proof of Stake support on the Stratis token the node is using [NStratis](https://github.com/stratisproject/NStratis) which is a POS implementation of NBitcoin.  

[.NET Core](https://dotnet.github.io/) is an open source cross platform framework and enables the development of applications and services on Windows, macOS and Linux.  

Join our community on [discord](https://discord.gg/9tDyfZs).  

The design
----------

**A Modular Approach**

A Blockchain is made of many components, from a FullNode that validates blocks to a Simple Wallet that track addresses.
The end goal is to develop a set of [Nuget](https://en.wikipedia.org/wiki/NuGet) packages from which an implementer can cherry pick what he needs.

* **NBitcoin**
* **Stratis.Bitcoin.Core**  - The bare minimum to run a pruned node.
* **Stratis.Bitcoin.Store** - Store and relay blocks to peers.
* **Stratis.Bitcoin.MemoryPool** - Track pending transaction.
* **Stratis.Bitcoin.Wallet** - Send and Receive coins
* **Stratis.Bitcoin.Miner** - POS or POW
* **Stratis.Bitcoin.Explorer**


Create a Blockchain in a .NET Core style programming
```
  var node = new FullNodeBuilder()
   .UseNodeSettings(nodeSettings)
   .UseConsensus()
   .UseBlockStore()
   .UseMempool()
   .AddMining()
   .AddRPC()
   .Build();

  node.Run();
```

What's Next
----------

We plan to add many more features on top of the Stratis Bitcoin blockchain:
Sidechains, Private/Permissioned blockchain, Compiled Smart Contracts, NTumbleBit/Breeze wallet and more...

Running a FullNode
------------------

The master branch is actively developed and regularly committed to, and it can become unstable.  
To compile a stable (production) release use any of the release tags form version 3.0.x.x and upwards.  

```
git clone https://github.com/stratisproject/StratisFullNode.git  
cd StratisFullNode\src

dotnet build

```

To run on the StraxTest network:
```
cd Stratis.StraxD
dotnet run -testnet
```  

Development
-----------
Up for some blockchain development?

Check this guides for more info:
* [Contributing Guide](Documentation/contributing.md)
* [Coding Style](Documentation/coding-style.md)

There is a lot to do and we welcome contributers developers and testers who want to get some Blockchain experience.
You can find tasks at the issues/projects or visit the dev_general channel on [discord](https://discord.gg/9tDyfZs).

Testing
-------
* [Testing Guidelines](Documentation/testing-guidelines.md)
