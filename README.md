<picture>
  <source media="(prefers-color-scheme: dark)" srcset=".github/logo-dark.png">
  <img src=".github/logo-light.png" alt="OpenKogama Server" width="400">
</picture>

## Launcher servers

To host a server for other players, follow [HOSTING.md](HOSTING.md). To let others play one of your worlds without hosting a server, click **Host** next to it in the launcher, which shares it with a code ([how it works](HOSTING.md#sharing-a-world-with-a-code)).

An optional `server.json` in the server working directory configures its launcher tile and the world opened by Join:

```json
{
  "title": "My KoGaMa server",
  "thumbnail": "server.png",
  "joinWorld": 1,
  "publicUrl": "https://example.com",
  "listenHost": "+",
  "clients": ["3.5.20", "1.25.12.280-1.32.10.547"],
  "mixedClients": false,
  "keepPlayChanges": false,
  "joinMode": "play",
  "admins": ["203.0.113.7"]
}
```

Without this file the server keeps its local addresses. `joinWorld` defaults to the first saved world. Set `publicUrl` to the HTTP/HTTPS address players use, and `listenHost` to the HTTP listener hostname (`+` accepts all hostnames); hosting requires HTTP port 8080 and Photon port 5055 to be reachable. On Windows the listener prefixes may require an HTTP URL reservation for the account running the server. Session responses use the public hostname for Photon and the public base URL for callbacks, the API and asset bundles. Clients built with Unity 2022 or newer, such as 3.5.20, refuse plain HTTP to anything but localhost, so a public server needs an HTTPS `publicUrl`, for example behind Caddy.

`clients` limits which game versions can join, as exact versions or inclusive ranges like `1.25.12.280-1.32.10.547`; the 2012 build is `2012`. Without it every version is accepted. Other versions get a 403 from `/session`, and the launcher picks an installed version that matches. `mixedClients` puts every version into one world session instead of one per translated protocol; it is unsupported, like `--mixed-clients`. `keepPlayChanges` saves what players change in play mode, such as cubes and moved vehicles, apart from the published world, so the world carries on after everyone leaves or the server restarts. It is saved every minute, when the last player leaves and when the server stops; a save that cannot be read back is skipped, the previous save is kept, and a world whose newest save does not load falls back to the previous one and then to the published world. Publishing the world again starts it fresh. `joinMode` set to `build` makes Join open the world in build mode, so everyone builds together; on the launcher's own server it only applies to players who join with a code, and the host's Play still plays. Players who join with a code go wherever the host is in that world, play or build, and `joinMode` only decides while the host is in neither. `admins` lists the IP addresses whose players can use admin commands such as `!reload` and get the game's admin tools to kick or ban players, besides players on the server's own machine, so whoever hosts a world with a code is its admin. Bans are kept in `bans.json` with the player's nickname, by IP address, or for players who joined with a code by profile and by an id the tunnel derives from their IP, so a new nickname does not get them back in. The id is a keyed hash, the key stays in `guests.key` next to `bans.json`, and the IP itself is never saved. `public` set to `true` announces the server to the list at `https://list.openkogama.org` every minute, and the launcher shows it to everyone under Public on the Servers tab; the list only shows a server whose `/api/server` answers at its `publicUrl` with `public` set, and drops it a few minutes after it stops. The player count on the list comes from the launchers of the people playing, once a minute and once per IP address, not from the server. `openkogama-server list --port 8099` runs such a list, and `list` in `server.json` points a server at another one. The launcher keeps the player's game settings, active avatar and own models in `profile` in its data folder, sends them to `/api/player` before the game starts and reads the settings back from `/api/settings` after it closes. `playerSettings`, `playerAvatars` and `playerInventory`, all on by default, decide what a server takes; it only takes avatars and models on servers where every nickname has its own profile.

The launcher edits the `server.json` of its own server under File, Server settings, and restarts the server on save. File, Admin lists who is playing on it with Kick and Ban, the bans with Unban, and the admin IP addresses, which apply at once and are saved into `server.json`. It uses `/api/admin/...`, which only answers on the server's own machine and only on a server whose `publicUrl` is local. A world hosted with a code follows it like a public server: the title, thumbnail and versions show on the tile of everyone who joins, and `joinMode`, `mixedClients`, `keepPlayChanges` and the plugins apply to them. With `public` set, the hosted world shows on the public list by its code while it is hosted.

`GET /api/server` exposes only `title` (up to 40 characters), `clients` and an optional `/api/server/thumbnail` path. The image endpoint serves the configured file up to 256 KB; PNG, JPEG and WebP are accepted by the launcher. `GET /session?mode=play` opens the configured entry world. Lists, map selection and hubs belong inside the game; the launcher never reads a remote server's world or avatar lists.

## Plugins

A plugin is a .NET class library that references `api/openkogama-api.csproj` and implements `IPlugin`. Put the dll in the `plugins` folder next to the server and it loads on start. To try a new build without restarting, replace the dll and type `!reload` in chat (`/reload` on clients before 3.0): every plugin is unloaded and loaded again. Only players on the server's own machine or from an IP listed in `admins` in `server.json` can reload. A plugin that starts its own timers or threads stops them in `Unload`, which also runs when the server stops, before the last save, so a plugin can undo temporary changes there. `examples/` has three plugins: `welcome`, `xp` and `sandbox`.

```csharp
public sealed class MyPlugin : IPlugin
{
    public void Load(IServer server)
    {
        server.AddCommand("hello", (player, args) => player.Message($"Hi {player.Name}"));
        server.Templates.Add("flat", "Flat World", world => world.Terrain.Fill(-32, 0, -32, 32, 0, 32, 19));
    }
}
```

Players type commands as `/hello` or `!hello`. Game versions from 3.0 on keep slash commands to themselves, so `!hello` is the one that works everywhere.

| Area | What you get |
|---|---|
| Server | `Chat`, `PlayerJoined`, `PlayerLeft`, `Tick` every second, `Update` every server tick (50 ms) with the seconds since the last one, `PlayerKilled`, `PlayerHit` (a player hurt another player with a weapon, with the victim first), `PlayerKilledByNpc`, `ItemCollected`, `RoundStarted`, `ObjectAdded`, `ObjectRemoved`, `NpcDamaged`, `NpcKilled`, `PlayerGestured` with a `Gesture` (`Wave`, `Nod`, `HeadShake`), `PlayerAnimated` with the `AvatarAnimation` a player's avatar switches to (`Idle`, `Walk`, `Jump`, `Swim`, `Dead`, `TPose`, or `Unknown` for a state the enum does not have yet, which the server logs once), `PlayerSwitchedItem` and `PlayerPickedUp` with an `AvatarItem` (`Hand` is an empty hand), `Players`, `Worlds`, `Broadcast`, `Notify`, `Announce`, `AddIcon` (a png shown as the badge of an announcement, registered before players join), `AddCommand`, `Storage`, `Templates` |
| Templates | `Add(id, name, kgmap bytes)`, `Add(id, name, kgmap bytes, build)` and `Add(id, name, build, baseTemplate)`; they show up under New world in the launcher and the site |
| World | `Objects`, `Find`, `Markers(name)` (every Text Message cube whose whole text is `$$name`, ignoring case and text tags; in play the server sets their text size to 0 so players do not see them), `LineOfSight(from, to)` (true when no cube is in the way), `Toggled(id)` and `SetToggled(id, on)` (read or switch a Toggle Box, and the world's logic follows), `Press(id)` (steps on a Pressure Plate or Trigger Cube for 0.3 s, as if a player did, so the world's logic fires on every client), `Add`, `Remove` (its wires go with it), `Link(from, to)` (wires one logic object's output to another's input, as in the editor), `Move` (at once, or gliding there over `seconds`), `SetData` for one key or many, `Terrain`, `Players`, `Broadcast`, `Notify` (a popup notification, chat on clients without one); changes reach players right away |
| Npc | `World.SpawnNpc(name, position, yaw, skinOf)` or `SpawnNpc(name, position, yaw, skin, size, level)` gives a real avatar, off the player list, with the skin of a player or a saved `AvatarSkin` (`Player.Skin`, `ToJson`, `FromJson`); moves with the same physics as a player (gravity, cube collisions, slopes, jumps, bouncy materials); `WalkTo`, `Stop`, `Jump`, `Teleport`, `Face`, `Speed`, `OnFire` (burning look), `Holding` (an item in hand like Bazooka or Sword), `Shoot` (fires the held item for everyone to see, damage is up to the plugin; `Shoot(direction, holdSeconds)` keeps the trigger pulled, like charging the Colossus laser), `Aim` (turns the line of fire without shooting), `EyePosition`, `BecomeColossus(trigger)` (the npc takes a free Colossus trigger: it grows to the trigger's size, holds the Colossus laser and stays put; clients from 1.39 to 2.3 show it) or `BecomeColossus(size)` with a `ColossusSize` (the same where the npc stands, no trigger needed, only without the trigger's shock wave and kill zone; a respawn turns it back), `Size` (0.01 to 10, scales the body and its collisions on every client through the mouse or growth pill, applied out of sight so the npc shows up a second later without the pill sound), `Level`, `Grounded`, `PlayAnimation` (an `AvatarAnimation`, or a state name, with `Unknown` or null for automatic), `AnimationState`, `Health`, `MaxHealth` (the health bar follows it), `Alive`, `Damage`, `Respawn`, `Remove`; player hits deal damage, knockback, burning and death like on a player, then the body hides after 2.5 s until `Respawn` |
| Terrain | `Set`, `Remove`, `Fill`, `Clear`, `Count` on the world terrain, coordinates in cubes, materials by id |
| Player | `Name`, `ProfileId`, `Actor`, `World`, `Position`, `Alive`, `IsAdmin` (on the server's machine or from an IP in `admins`), `Team` and `SetTeam(team)` (moves the player to a team the world has, by its spawn points or class cubes, and the game respawns them there; false for a team the world lacks), `Holding` (the `AvatarItem` in hand), `Skin`, `Xp`, `AddXp`, `Gold`, `AddGold`, `Message` (rich text like `<b>` and `<color=#ff8800>` works, and clients without rich text get it stripped), `Say`, `Notify`, `Damage` (an npc hit with knockback that the game applies like a real one), `Equip(item)` (puts an `AvatarItem` in a playing player's hand like a pickup would, `Hand` empties it), `GiveLightningSpeed()` (a Lightning Speed pickup only that player sees appears around them and their game takes it, three times the speed for 7 seconds), `PressLocally(id)` (steps on a Pressure Plate or Trigger Cube for 0.3 s only in this player's game, so only their copy of the world's logic reacts, on clients that run the logic themselves), `Teleport(position, method)` with a `TeleportMethod`: `HiddenVehicle` seats the player in an invisible hovercraft at the target and throws them out, in about half a second, on clients that know the forced vehicle exit; `Teleporter` puts a pair of teleporters scaled a million times at the target, so the player is inside them wherever they run and the pads end up far underground, and the game moves the player there with its own fade, about 4 seconds, while the pair's light shines at the target until the pair goes after 1.5 seconds; either way only that player gets the helper objects, `EnterVehicle` (puts a playing player in the driver seat of a hovercraft, monoplane, jetpack or hamster wheel, or of a new one from a vehicle spawner, without asking), `BecomeColossus` (makes the player the Colossus of a free Colossus trigger, only on clients that have the Colossus; clients from 1.47 to 1.92 cannot leave it without errors, a bug in their laser), `Kick` |
| Storage | key and value strings per plugin, saved in `plugins/data` |

## Credits

Made by nightus.

- **Becko**: huge thanks, the main inspiration for this project, and the packet captures
- **LazyLemon**: info on how things looked in old versions, and help with the launcher
- **bustersky**: archiving the standalone builds
- **kamilslimak**: finding the 2012 build
