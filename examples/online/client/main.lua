local players = ""
local ping

on("message", function(name, data)
  if name == "online:players" then
    if players ~= data then print("online: " .. data) end
    players = data
  elseif name == "online:pong" then
    ping = (os.clock() - tonumber(data)) * 1000
    print(string.format("pong after %.0f ms", ping))
  end
end)

every(5, function()
  server.send("online:ping", tostring(os.clock()))
end)

on("draw", function()
  if players == "" then return end
  local width = screen.size()
  local text = "Online: " .. players
  local w = draw.measure(text, 16) + 28
  draw.rect(width - w - 16, 16, w, ping and 56 or 36, "#101318d8", { rounding = 10 })
  draw.text(text, width - w - 2, 24, { size = 16 })
  if ping then
    draw.text(string.format("ping %.0f ms", ping), width - w - 2, 44, { size = 13, color = "#9fc7ff" })
  end
end)
