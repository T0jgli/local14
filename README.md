# Impulsum14

<img width="256" height="256" alt="c39712e5243e23372f53b2990ab42afe" src="https://github.com/user-attachments/assets/bcc9f83d-1865-4bb5-9967-5a0392f23c9d" />

A backend server for a game called "FIFA 14" PC's online mode to access Ultimate team.

## Features

- Origin authentication
- Redirector server
- Blaze server
- OSDK Configs
- OSDK web service
- DIME Confg
- FUT/EASFC accountinfo
- EASFC RS4 server redirect
- Persistent user settings
- Persistent account profile
- POW/EASFC/EASW web service (Base)
- EA CDN recreation
- FUT Hub
- FUT DB (99% done)
- Packs
- Proper Pack Weights
- Squad saving
- Club search
- TOTW 
- Tournaments
- Match Stats

## TODO

- legends
- Transfermarket
- divisions
- manager challenges
- minor bug fixes

## Known Bugs

- Fitness coaches and Physios crashes the game

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
dotnet run --project Impulsum14
```
(You can also compile using visual studio)

## License

Apache License Version 2.0

## Disclaimer

This project is an unofficial, community-developed backend. It is not affiliated with, endorsed by, or sponsored by Electronic Arts Inc. "FIFA" is a trademark of its respective owner and is used solely for identification and compatibility purposes.

## Credits
 
- MYorderlyHuman on discord (Helped soo much in understanding of the architecture)
- Toniboi on discord (Helped with tournaments)
- Draz on discord (Helped with Manager cards and cert bypass)
- [BlazeSDK](https://github.com/Aim4kill/BlazeSDK) By [@Aim4kill](https://github.com/Aim4kill)
- [Zamboni3](https://github.com/ZamboniDevelopment/Zamboni3) By [@ZamboniDevelopment](https://github.com/ZamboniDevelopment)
