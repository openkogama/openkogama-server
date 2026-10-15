# Hosting a server

Anyone can host an OpenKogama server and let others join it from the Servers tab in the launcher.

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
| `admins` | IP addresses that may type `!reload` to load changed plugins without a restart. Players on the server's own machine always can. |
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

In the launcher, open Servers, click **Add server...** and enter your `https://` address. Send the same address to your friends.

## Good to know

- Every nickname set in the launcher gets its own profile, with its own level, gold and avatars. There are no passwords yet, so anyone can join under any nickname, and players without a nickname share one profile.
- So far only the 3.5.x builds have been tested on a remote server.
- Back up `server.db` now and then. It holds every world and profile.
- Plugins go in the `plugins` folder inside the data folder, see the [README](README.md#plugins).
- Got it running and want it in the launcher's built-in server list? Message nightus on the [OpenKogama Discord](https://discord.gg/u6tKuP3k4M).
