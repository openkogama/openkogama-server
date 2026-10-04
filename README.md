# openkogama-server

## Plugins

A plugin is a .NET class library that references `api/openkogama-api.csproj` and implements `IPlugin`. Put the dll in the `plugins` folder next to the server and it loads on start. `examples/` has three plugins: `welcome`, `xp` and `sandbox`.

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

| Area | What you get |
|---|---|
| Server | `Chat`, `PlayerJoined`, `PlayerLeft`, `Tick` every second, `Update` every server tick (50 ms) with the seconds since the last one, `PlayerKilled`, `ItemCollected`, `RoundStarted`, `ObjectAdded`, `ObjectRemoved`, `NpcDamaged`, `NpcKilled`, `Players`, `Worlds`, `Broadcast`, `Notify`, `Announce`, `AddIcon` (a png shown as the badge of an announcement, registered before players join), `AddCommand`, `Storage`, `Templates` |
| Templates | `Add(id, name, kgmap bytes)`, `Add(id, name, kgmap bytes, build)` and `Add(id, name, build, baseTemplate)`; they show up under New world in the launcher and the site |
| World | `Objects`, `Find`, `Add`, `Remove`, `Move`, `SetData` for one key or many, `Terrain`, `Players`, `Broadcast`, `Notify` (a popup notification, chat on clients without one); changes reach players right away |
| Npc | `World.SpawnNpc(name, position, yaw, skinOf)` or `SpawnNpc(name, position, yaw, skin, size, level)` gives a real avatar, off the player list, with the skin of a player or a saved `AvatarSkin` (`Player.Skin`, `ToJson`, `FromJson`); moves with the same physics as a player (gravity, cube collisions, slopes, jumps, bouncy materials); `WalkTo`, `Stop`, `Jump`, `Teleport`, `Face`, `Speed`, `OnFire` (burning look), `Holding` (an item in hand like Bazooka or Sword), `Shoot` (fires the held item for everyone to see, damage is up to the plugin), `Size` (0.01 to 10, scales the body and its collisions on every client through the mouse or growth pill, applied out of sight so the npc shows up a second later without the pill sound), `Level`, `Grounded`, `PlayAnimation` (Idle, Walk, Jump, Swim, Dead, null for automatic), `Health`, `MaxHealth` (the health bar follows it), `Alive`, `Damage`, `Respawn`, `Remove`; player hits deal damage, knockback, burning and death like on a player, then the body hides after 2.5 s until `Respawn` |
| Terrain | `Set`, `Remove`, `Fill`, `Clear`, `Count` on the world terrain, coordinates in cubes, materials by id |
| Player | `Name`, `ProfileId`, `Actor`, `World`, `Position`, `Skin`, `Xp`, `AddXp`, `Gold`, `AddGold`, `Message`, `Say`, `Notify`, `Damage` (an npc hit with knockback that the game applies like a real one), `Kick` |
| Storage | key and value strings per plugin, saved in `plugins/data` |

## Credits

Made by nightus.

- **Becko**: huge thanks, the main inspiration for this project, and the packet captures
- **LazyLemon**: info on how things looked in old versions, and help with the launcher
- **bustersky**: archiving the standalone builds
- **kamilslimak**: finding the 2012 build
