# FIFA Server 14

A emulated backend server for FIFA 14 PC's online mode to access FUT.

## Features

- Full Blaze/Origin authentication (Login, SilentLogin, OriginLogin, ExpressLogin, LoginPersona, Logout, ListUserEntitlements)
- Redirector server (TLS, port 42127)
- Main Blaze server (plaintext, port 10000) handling all game logic
- Post-login notifications (UserSessions, CensusData)
- URL provisioning
- OSDK Configs

## SDK

| Project | Description |
|---|---|
| **EATDF** | EA TDF (Type Definition Format)|
| **ProtoFire** | Fire/Fire2 wire framing protocol|
| **Blaze.Core** | Core Blaze server infrastructure|
| **Blaze3SDK** | Blaze 13 component SDK|

## Requirements

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build & Run

```sh
dotnet restore
dotnet build
dotnet run --project FIFAServer14
```
(You can also compile using visual studio)

## License

This project is not affiliated with Electronic Arts Inc. or any properties mentioned in this project. This is an independent, community-driven project.

## Credits
 
- MYorderlyHuman on discord (Help so much in understanding of the architecture)
- Draz on discord (Cert Exploit, and authentication component)
- [BlazeSDK](https://github.com/Aim4kill/BlazeSDK) By [@Aim4kill](https://github.com/Aim4kill)
- [Zamboni3](https://github.com/ZamboniDevelopment/Zamboni3) By [@ZamboniDevelopment](https://github.com/ZamboniDevelopment)