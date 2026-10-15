<picture>
  <source media="(prefers-color-scheme: dark)" srcset=".github/logo-dark.png">
  <img src=".github/logo-light.png" alt="OpenKogama Server" width="400">
</picture>

## Launcher servers

To host a server for other players, follow [HOSTING.md](HOSTING.md).

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
  "keepPlayChanges": false
}
```

Without this file the server keeps its local addresses. `joinWorld` defaults to the first saved world. Set `publicUrl` to the HTTP/HTTPS address players use, and `listenHost` to the HTTP listener hostname (`+` accepts all hostnames); hosting requires HTTP port 8080 and Photon port 5055 to be reachable. On Windows the listener prefixes may require an HTTP URL reservation for the account running the server. Session responses use the public hostname for Photon and the public base URL for callbacks, the API and asset bundles. Clients built with Unity 2022 or newer, such as 3.5.20, refuse plain HTTP to anything but localhost, so a public server needs an HTTPS `publicUrl`, for example behind Caddy.

`clients` limits which game versions can join, as exact versions or inclusive ranges like `1.25.12.280-1.32.10.547`; the 2012 build is `2012`. Without it every version is accepted. Other versions get a 403 from `/session`, and the launcher picks an installed version that matches. `mixedClients` puts every version into one world session instead of one per translated protocol; it is unsupported, like `--mixed-clients`. `keepPlayChanges` saves what players change in play mode, such as cubes and moved vehicles, apart from the published world, so the world carries on after everyone leaves or the server restarts. It is saved every minute, when the last player leaves and when the server stops; a save that cannot be read back is skipped, the previous save is kept, and a world whose newest save does not load falls back to the previous one and then to the published world. Publishing the world again starts it fresh.

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
| Server | `Chat`, `PlayerJoined`, `PlayerLeft`, `Tick` every second, `Update` every server tick (50 ms) with the seconds since the last one, `PlayerKilled`, `ItemCollected`, `RoundStarted`, `ObjectAdded`, `ObjectRemoved`, `NpcDamaged`, `NpcKilled`, `PlayerGestured` with a `Gesture` (`Wave`, `Nod`, `HeadShake`), `PlayerAnimated` with the `AvatarAnimation` a player's avatar switches to (`Idle`, `Walk`, `Jump`, `Swim`, `Dead`, `TPose`, or `Unknown` for a state the enum does not have yet, which the server logs once), `PlayerSwitchedItem` and `PlayerPickedUp` with an `AvatarItem` (`Hand` is an empty hand), `Players`, `Worlds`, `Broadcast`, `Notify`, `Announce`, `AddIcon` (a png shown as the badge of an announcement, registered before players join), `AddCommand`, `Storage`, `Templates` |
| Templates | `Add(id, name, kgmap bytes)`, `Add(id, name, kgmap bytes, build)` and `Add(id, name, build, baseTemplate)`; they show up under New world in the launcher and the site |
| World | `Objects`, `Find`, `Add`, `Remove`, `Move` (at once, or gliding there over `seconds`), `SetData` for one key or many, `Terrain`, `Players`, `Broadcast`, `Notify` (a popup notification, chat on clients without one); changes reach players right away |
| Npc | `World.SpawnNpc(name, position, yaw, skinOf)` or `SpawnNpc(name, position, yaw, skin, size, level)` gives a real avatar, off the player list, with the skin of a player or a saved `AvatarSkin` (`Player.Skin`, `ToJson`, `FromJson`); moves with the same physics as a player (gravity, cube collisions, slopes, jumps, bouncy materials); `WalkTo`, `Stop`, `Jump`, `Teleport`, `Face`, `Speed`, `OnFire` (burning look), `Holding` (an item in hand like Bazooka or Sword), `Shoot` (fires the held item for everyone to see, damage is up to the plugin), `Size` (0.01 to 10, scales the body and its collisions on every client through the mouse or growth pill, applied out of sight so the npc shows up a second later without the pill sound), `Level`, `Grounded`, `PlayAnimation` (an `AvatarAnimation`, or a state name, with `Unknown` or null for automatic), `AnimationState`, `Health`, `MaxHealth` (the health bar follows it), `Alive`, `Damage`, `Respawn`, `Remove`; player hits deal damage, knockback, burning and death like on a player, then the body hides after 2.5 s until `Respawn` |
| Terrain | `Set`, `Remove`, `Fill`, `Clear`, `Count` on the world terrain, coordinates in cubes, materials by id |
| Player | `Name`, `ProfileId`, `Actor`, `World`, `Position`, `Holding` (the `AvatarItem` in hand), `Skin`, `Xp`, `AddXp`, `Gold`, `AddGold`, `Message`, `Say`, `Notify`, `Damage` (an npc hit with knockback that the game applies like a real one), `Kick` |
| Storage | key and value strings per plugin, saved in `plugins/data` |

## Credits

Made by nightus.

- **Becko**: huge thanks, the main inspiration for this project, and the packet captures
- **LazyLemon**: info on how things looked in old versions, and help with the launcher
- **bustersky**: archiving the standalone builds
- **kamilslimak**: finding the 2012 build
