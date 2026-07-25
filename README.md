# FIFAServer14

<img width="256" height="256" alt="c39712e5243e23372f53b2990ab42afe" src="https://github.com/user-attachments/assets/bcc9f83d-1865-4bb5-9967-5a0392f23c9d" />

A backend server for FIFA 14 PC's online mode to access FUT.

## Features

- Full Blaze/Origin authentication (Login, SilentLogin, OriginLogin, ExpressLogin, LoginPersona, Logout, ListUserEntitlements)
- Redirector server (TLS, port 42127)
- Main Blaze server (plaintext, port 10000) handling all game logic
- Post-login notifications (UserSessions, CensusData)
- URL provisioning
- OSDK Configs
- OSDK web service (HTTP, port 9988) handling the game's web-service calls
- DIME config routing (cfgrouting.xml / dimerouting.xml)
- DIME store config (dimecfg / storecfg / storedesc) + sponsored events
- FUT boot config (futBoot.xml)
- FUT/EASFC accountinfo
- EASFC RS4 server redirect
- Persistent user settings
- Persistent account profile (email/country/DOB/opt-ins)
- POW/EASFC/EASW web service
- EA CDN recreation
- FUT Hub
- FUT DB (80% done)
- Packs
- Squad saving
- Club search

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

Apache License Version 2.0

## Disclaimer

This project is an unofficial, community-developed backend. It is not affiliated with, endorsed by, or sponsored by Electronic Arts Inc. "FIFA" is a trademark of its respective owner and is used solely for identification and compatibility purposes.

## Credits
 
- MYorderlyHuman on discord (Helped soo much in understanding of the architecture)
- [BlazeSDK](https://github.com/Aim4kill/BlazeSDK) By [@Aim4kill](https://github.com/Aim4kill)
- [Zamboni3](https://github.com/ZamboniDevelopment/Zamboni3) By [@ZamboniDevelopment](https://github.com/ZamboniDevelopment)
