# FishyUTP
A Unity Transport (UTP) implementation for Fish-Net, a A feature-rich Unity networking solution aimed towards reliability, ease of use, efficiency, and flexibility.

## Need Help?

This transport does not have first-party support and is a community maintained project.

- Raise an [Issue](https://github.com/matthewshirley/FishyUTP/issues) or start a [Discussion](https://github.com/matthewshirley/FishyUTP/discussions) for questions or bugs regarding FishyUTP.
- Create a [Pull Request](https://github.com/matthewshirley/FishyUTP/pulls) to propose a change or fix a bug.
- Chat in the [Fish-Net Discord](https://discord.gg/fishnetworking) if you have questions about the Fish-Net library.

## Compatibility

This transport library is in early development and should not be used for a production project.

<details><summary>Feature Compatibility</summary>

The following is the anticipated features this library will support in the future. Propose new features via GitHub Issues or Pull Request.

| Feature         | Supported |
|-----------------| --------- |
| Transport       | 🔨         |
| Jobified        | ❌         |
| Relay | 🔨         |

- ✅ -- Implemented
- 🔨 -- Partially implemented
- ❌ -- Not implemented
</details>

## Install

### Install Dependencies

Ensure you have installed the required dependencies using the Unity Package Manager:

* [Fish-Net: Networking Evolved](https://assetstore.unity.com/packages/tools/network/fish-net-networking-evolved-207815)
* [com.unity.transport](https://docs-multiplayer.unity3d.com/transport/current/install) 
* [com.unity.services.relay](https://docs.unity.com/relay/get-started.html)

### Install Transport

#### via git

   * Open the Unity Package Manager by navigating to Window > Package Manager
   * Click Add in the status bar.
   * Select "Add package via Git URL"
   * Input "https://github.com/matthewshirley/FishyUTP.git?path=/FishNet/Plugins/FishyUTP"
   * Click "Add" 

#### Manual

Move the `FishNet` folder from this repository to the `Assets` folder in your Unity project.

#### Transport Component

Add the "FishyUTP" transport component to the "NetworkManager" game object. 

### Configure Relay

FishyUTP supports creating and joining Unity Relay allocations to simplify and secure network connectivity. This is an optional feature that can be disable on FishyUTP transport ("Use Relay").

* Add the "FishyUTP Relay Manager" component to the game object that contains the "FishyUTP" and "NetworkManager" component.
* On the "FishyUTP" component:
  * Enable "Use Relay"
  * Enable "Login To Unity Services" if you are not already doing so elsewhere in your application

The "Join Code" on the "FishyUTP Relay Manager" represents either the join code generated for the host allocation or the join allocation the client will attempt to connect to. 
