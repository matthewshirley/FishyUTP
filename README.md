# FishyUTP
A Unity Transport (UTP) implementation for Fish-Networking, a A feature-rich Unity networking solution aimed towards reliability, ease of use, efficiency, and flexibility.

## Need Help?

- Chat in the [FishNet Discord](https://discord.gg/fishnetworking)

## Compatibility

This transport library is in early development and should not be used for a production project.

<details><summary>Feature Compatibility</summary>

The following is the anticipated features this library will support in the future. Propose new features via GitHub Issues or Pull Request.

| Feature         | Supported |
|-----------------| --------- |
| Transport       | 🔨         |
| Jobified        | ❌         |
| Relay | ❌         |

- ✅ -- Implemented
- 🔨 -- Partially implemented
- ❌ -- Not implemented
</details>

## Install

### Install Dependencies

Ensure you have installed the required dependencies using the Unity Package Manager:

* [FishNet](https://assetstore.unity.com/packages/tools/network/fish-net-networking-evolved-207815)
* [com.unity.transport](https://docs-multiplayer.unity3d.com/transport/current/install) 

### Install Transport

#### via git

   * Open the Unity Package Manager by navigating to Window > Package Manager
   * Click Add in the status bar.
   * Select "Add package via Git URL"
   * Input the URL to this repository
   * Click "Add" 

#### Manual

Move the `FishNet` folder from this repository to the `Assets` folder in your Unity project.

### Add Transport

Add the "FishyUTP" transport component to the "NetworkManager" game object. 
