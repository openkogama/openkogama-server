# Hosting a server

Anyone can host an OpenKogama server and let others join it from the Servers tab in the launcher.

## Sharing a world with a code

To let others play one of your worlds you don't need any of this. In the launcher, select a world on the Worlds tab and click **Host**. The game starts and a bar at the top of the launcher shows a code like `ABC-DEF`. Others click **Join with code** on the Servers tab, type the code and play with the same game version. Nobody opens ports or needs an IP address: the launchers find each other through small meeting points in Europe, North America, South America and Asia and connect directly. The host uses the nearest one, and when a direct connection can't be made, the game goes through it instead. Keep the launcher open while others play. With `"public": true` in File, Server settings, the hosted world also shows for everyone under Public on the Servers tab while you host.

The same works from the command line. `openkogama-server tunnel host` shares a server running on the same PC (`--http 8080 --photon 5055` by default) and prints its code, `openkogama-server tunnel join CODE` prints a local port that leads to that server, and `openkogama-server rendezvous --port 5058` runs a meeting point of your own, which both sides then pick with `--rendezvous host:port` (several are separated by commas). Players who come through a tunnel get their own profile by nickname, are never admins, and can't reach `/shutdown`, `/api/worlds`, `/api/avatars` or `/api/templates`.

The rest of this guide is for a server that stays online for everyone.

## What you need

- A PC or VPS that stays on. One CPU core and 1 GB of RAM is enough for a few players.
- These ports open to the internet: TCP 80 and 443 for HTTPS, UDP 5055 for the game, and TCP 843 for the 2012 to 2014 web player builds. At home, forward them on your router.
- An HTTPS address. Builds made with Unity 2022 or newer, such as 3.5.x, refuse plain HTTP to anything but localhost. Without a domain, `<your-ip-with-dashes>.sslip.io` (for example `203-0-113-7.sslip.io`) points at your IP and works with Caddy below.

By default the server uses ports 8080, 5055, 843 to 845 and 8081 on its machine, the same as the launcher's own local server, so don't play local worlds in the launcher on that PC while it runs.

## 1. Get the server

**Windows:** install the launcher, then copy `%LocalAppData%\OpenKogamaLauncher\current\server` to a folder of its own, for example `C:\OpenKogamaServer\app`. It runs without installing .NET. Copy it again after a launcher update to update the server.

**Linux:** install the .NET 10 SDK, clone this repository and [openkogama-photon](https://github.com/openkogama/openkogama-photon) next to each other, then build:

```sh
dotnet publish openkogama-server/openkogama-server.csproj -c Release -o app -p:UseAppHost=false
```

## 2. Make a data folder

The server reads its program files from `app` and keeps everything else in the folder it runs from: `server.json`, `server.db` with the worlds and profiles, downloaded asset bundles, `logs` and `plugins`. Make a folder such as `C:\OpenKogamaServer\data` or `/opt/openkogama/data` and put a `server.json` in it:

```json
{
  "title": "My KoGaMa server",
  "thumbnail": "thumbnail.jpg",
  "publicUrl": "https://203-0-113-7.sslip.io",
  "mixedClients": true,
  "keepPlayChanges": true
}
```

| Field | Meaning |
| --- | --- |
| `title` | Name on the launcher tile, up to 40 characters. |
| `thumbnail` | PNG, JPEG or WebP in the data folder, up to 256 KB, shown cropped to 400 × 160. |
| `publicUrl` | The HTTPS address players use. |
| `joinWorld` | Id of the world players join. Leave it out to use the world played last. |
| `clients` | Versions you accept, exact like `"3.5.20"` or ranges like `"1.25.12.280-1.32.10.547"`. Leave it out to accept all. |
| `mixedClients` | `true` puts every version into the same world. Checked with builds from 2012 to 3.5 playing together. |
| `keepPlayChanges` | `true` keeps the cubes, vehicles and other changes players make, after they leave and after restarts. |
| `joinMode` | `"build"` puts everyone who joins straight into the editor of the join world, and their changes are saved into the world itself. The default is `"play"`. |
| `public` | `true` shows the server to everyone under Public on the launcher's Servers tab. The list checks the server at its `publicUrl` before showing it, and drops it a few minutes after it stops. |
| `playerSettings` | `true` by default: mouse and graphics settings come from the player's launcher and go back to it after the game. `false` keeps them for one session only. |
| `playerAvatars` | `true` by default: players wear the avatar they use in their own launcher. `false` keeps the avatars they have on this server. |
| `playerInventory` | `true` by default: the models from the player's own inventory come along into theirs on this server. `false` keeps only what they have here. |
| `antiCheatCrash` | `true` by default: chat, nicknames and text objects lose the rich text tags that crash the game, like `<quad>` or a broken `<size>`, game messages only the server sends are dropped, and so are broken terrain events that would crash players who join later. Chat keeps `<b>`, `<i>` and `<color>`, and text objects keep the rest of their formatting. |
| `antiCheatSpam` | `true` by default: a player can send at most 8 chat messages in 5 seconds and 40 terrain events in 2 seconds. |
| `antiCheatSpoof` | `true` by default: nobody can write in chat as another player or report someone else's death. |
| `antiCheatBuild` | `true` by default: in a play session nobody can do what only the editor does, like placing, removing, linking or changing objects, and players move and change only their own avatar and vehicles. In every session the cube gun builds only with the materials of the map's cube guns, and only destructible materials can be destroyed. |
| `admins` | IP addresses of admins. Admins may type `!reload` to load changed plugins without a restart and get the game's own admin tools on the player list, where they can kick or ban other players. Bans are kept in `bans.json` in the data folder. Players on the server's own machine are always admins. |
| `httpPort`, `photonPort` | Ports for HTTP and the game, 8080 and 5055 by default. Change them to run a second server on the same machine; the 2012 build always connects to 5055. |

To bring your own worlds, close the launcher and copy `%LocalAppData%\OpenKogama\OpenKogama Launcher\server\server.db` into the data folder.

## 3. Run it

**Windows**, from the data folder:

```bat
cd C:\OpenKogamaServer\data
..\app\openkogama-server.exe
```

**Linux**, from the data folder:

```sh
cd /opt/openkogama/data
dotnet /opt/openkogama/app/openkogama-server.dll
```

To keep it running on Linux and restart it after a crash, create a user with `useradd --system openkogama`, give it the data folder with `chown -R openkogama /opt/openkogama/data` and add `/etc/systemd/system/openkogama.service`:

```ini
[Unit]
Description=OpenKogama server
After=network-online.target
Wants=network-online.target

[Service]
User=openkogama
WorkingDirectory=/opt/openkogama/data
ExecStart=/usr/bin/dotnet /opt/openkogama/app/openkogama-server.dll
Restart=on-failure
AmbientCapabilities=CAP_NET_BIND_SERVICE

[Install]
WantedBy=multi-user.target
```

Then start it with `systemctl enable --now openkogama`.

## 4. Put HTTPS in front

Install [Caddy](https://caddyserver.com) and use this `Caddyfile`, with your address instead of the example. Caddy gets the certificate by itself. The `@manage` lines keep world and avatar management and shutdown, which the launcher only uses on your own PC and which have no password yet, away from players:

```
203-0-113-7.sslip.io {
  @manage path /shutdown /api/templates* /api/worlds* /api/avatars*
  respond @manage 403
  reverse_proxy 127.0.0.1:8080 {
    header_up Host 127.0.0.1:8080
  }
}
```

Leave `listenHost` out of `server.json` so the server only listens on its own machine and Caddy stays the only way in. Don't open port 8080.

## 5. Join it

In the launcher, open Servers, click **Add server...** and enter your `https://` address. Send the same address to others, or make the server public so it shows for everyone.

## Good to know

- Every nickname set in the launcher gets its own profile, with its own level, gold and avatars. There are no passwords yet, so anyone can join under any nickname, and players without a nickname share one profile.
- So far only the 3.5.x builds have been tested on a remote server.
- Back up `server.db` now and then. It holds every world and profile.
- Plugins go in the `plugins` folder inside the data folder, see the [README](README.md#plugins).
- Want everyone to see it in the launcher? Add `"public": true` to `server.json` and restart the server. It shows under Public on the Servers tab within a minute.
