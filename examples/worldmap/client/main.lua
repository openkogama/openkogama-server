local key = "F4"
local zoomStep = 1.1
local maxPerUnit = 48
local startUnits = 100
local headSize = 48
local otherSize = 36
local buttonSize = 40
local buttonMargin = 20
local buttonGap = 8
local playersEvery = 0.2
local waitLimit = 60
local font = "segoeui.ttf"
local boldFont = "seguisb.ttf"
local teamColors = { ["0"] = "#3d8bff", ["1"] = "#ff453a", ["2"] = "#32d74b", ["3"] = "#ffd60a" }
local accent = "#ffd700"
local plain = "#ffffffd0"

local function makeHint()
  return {
    { "Drag", accent }, { " to move      ", plain },
    { "Scroll", accent }, { " to zoom      ", plain },
    { key, accent }, { ", ", plain }, { "Esc", accent }, { " or ", plain }, { "X", accent }, { " to close", plain },
  }
end

local hint = makeHint()

local map
local incoming
local open = false
local waiting = false
local waitingSince = 0
local notice
local noticeUntil = 0
local zoom = 1
local centerX, centerY = 0, 0
local dragX, dragY
local head
local myTeam = "-"
local others = {}
local clock = 0
local sentView

if input.consume then input.consume(key) end

local function ready(id)
  return not server.file or server.file(id) == "ready"
end

local function control(on)
  if input.block then input.block(on) end
  if input.cursor then input.cursor(on) end
end

local function view()
  local screenWidth, screenHeight = screen.size()
  local scale = math.min(screenWidth / map.width, screenHeight / map.height) * zoom
  return screenWidth, screenHeight, scale
end

local function clampCenter()
  centerX = math.max(0, math.min(map.width, centerX))
  centerY = math.max(0, math.min(map.height, centerY))
end

local function mapPoint(x, z)
  return (x - map.minX) / map.unit, map.height - (z - map.minZ) / map.unit
end

local function playerPoint()
  local px, _, pz = player.position()
  if not px then return nil end
  return mapPoint(px, pz)
end

local function zoomLimit()
  local screenWidth, screenHeight = screen.size()
  local fit = math.min(screenWidth / map.width, screenHeight / map.height)
  return math.max(1, maxPerUnit * map.unit / fit)
end

local function zoomAt(factor, atX, atY)
  local screenWidth, screenHeight, scale = view()
  local pointX = centerX + (atX - screenWidth / 2) / scale
  local pointY = centerY + (atY - screenHeight / 2) / scale
  zoom = math.max(1, math.min(zoomLimit(), zoom * factor))
  local _, _, newScale = view()
  centerX = pointX - (atX - screenWidth / 2) / newScale
  centerY = pointY - (atY - screenHeight / 2) / newScale
  clampCenter()
end

local function reset()
  zoom = 1
  centerX, centerY = map.width / 2, map.height / 2
  dragX, dragY = nil, nil
end

local function start()
  local x, y = playerPoint()
  if not x then return reset() end
  local screenWidth, screenHeight = screen.size()
  local fit = math.min(screenWidth / map.width, screenHeight / map.height)
  zoom = math.max(1, math.min(zoomLimit(), screenHeight / startUnits * map.unit / fit))
  centerX, centerY = x, y
  dragX, dragY = nil, nil
  clampCenter()
end

local function restore(saved)
  local x, z, value = (saved or ""):match("^(%-?%d+),(%-?%d+),(%d+)$")
  if not x then return start() end
  zoom = math.max(1, math.min(zoomLimit(), tonumber(value) / 1000))
  centerX, centerY = mapPoint(tonumber(x) / 100, tonumber(z) / 100)
  dragX, dragY = nil, nil
  clampCenter()
end

local function switch(new)
  if not map or not open then
    map = new
    return
  end
  local screenWidth, screenHeight, scale = view()
  local worldX = map.minX + centerX * map.unit
  local worldZ = map.minZ + (map.height - centerY) * map.unit
  local perUnit = scale / map.unit
  map = new
  local fit = math.min(screenWidth / map.width, screenHeight / map.height)
  zoom = math.max(1, math.min(zoomLimit(), perUnit * map.unit / fit))
  centerX, centerY = mapPoint(worldX, worldZ)
  clampCenter()
end

local function closeButton()
  local screenWidth = screen.size()
  return screenWidth - buttonSize - buttonMargin, buttonMargin
end

local function findButton()
  local x, y = closeButton()
  return x - buttonSize - buttonGap, y
end

local function over(x, y)
  local mouseX, mouseY = input.mouse()
  return mouseX ~= nil and mouseX >= x and mouseX <= x + buttonSize and mouseY >= y and mouseY <= y + buttonSize
end

local function cross(x, y, color)
  local left, right = x + 8, x + buttonSize - 8
  local top, bottom = y + 8, y + buttonSize - 8
  local middleX, middleY = (left + right) / 2, (top + bottom) / 2
  local gap = 2 / math.sqrt(2)
  draw.line(left, top, right, bottom, color, 4)
  draw.line(right, top, middleX + gap, middleY - gap, color, 4)
  draw.line(middleX - gap, middleY + gap, left, bottom, color, 4)
end

local function target(x, y, color)
  draw.circle(x + buttonSize / 2, y + buttonSize / 2, buttonSize / 2 - 9, color, { thickness = 3 })
  draw.circle(x + buttonSize / 2, y + buttonSize / 2, 3.5, color)
end

local function button(icon, x, y)
  local hover = over(x, y)
  icon(x + 2, y + 2, hover and "#00000080" or "#00000040")
  icon(x, y, hover and "#ffffff" or "#ffffff70")
  return hover
end

local function remember()
  local x = map.minX + centerX * map.unit
  local z = map.minZ + (map.height - centerY) * map.unit
  local text = string.format("%d,%d,%d", math.floor(x * 100 + 0.5), math.floor(z * 100 + 0.5), math.floor(zoom * 1000 + 0.5))
  map.saved = text
  if text ~= sentView then
    sentView = text
    server.send("worldmap:view", text)
  end
end

local function toggle(value)
  if open and map and not value then remember() end
  open = value
  waiting = false
  if open then restore(map.saved) else others = {} end
  control(open)
  server.send("worldmap:open", open and "1" or "0")
end

local function shown(other)
  local t = math.min(1, (clock - other.at) / playersEvery)
  return other.fromX + (other.toX - other.fromX) * t, other.fromZ + (other.toZ - other.fromZ) * t
end

local function file(id)
  return id ~= "-" and id or nil
end

local function receive(data)
  local seen = {}
  local first = true
  for line in data:gmatch("[^\n]+") do
    if first then
      myTeam = line
      first = false
    else
      local profile, x, z, team, face, ring, name = line:match("^(%d+)\t(%S+)\t(%S+)\t(%S+)\t(%S+)\t(%S+)\t(.*)$")
      if profile then x, z = tonumber(x), tonumber(z) end
      if x and z then
        local other = others[profile]
        if other then
          other.fromX, other.fromZ = shown(other)
        else
          other = { fromX = x, fromZ = z }
          others[profile] = other
        end
        other.toX, other.toZ, other.at = x, z, clock
        other.team, other.face, other.ring, other.name = team, file(face), file(ring), name
        seen[profile] = true
      end
    end
  end
  for profile in pairs(others) do
    if not seen[profile] then others[profile] = nil end
  end
end

local function line(parts, x, y, size)
  local width = 0
  for _, part in ipairs(parts) do width = width + draw.measure(part[1], size, font) end
  x = x - width / 2
  for _, part in ipairs(parts) do
    draw.text(part[1], x, y, { size = size, color = part[2], font = font })
    x = x + draw.measure(part[1], size, font)
  end
end

local function marker(face, ring, size, color, x, y)
  draw.circle(x, y, size / 4 + 2, "#000000aa")
  draw.circle(x, y, size / 4, color)
  if not face or not ring then return end
  draw.image(ring, x - size / 2, y - size / 2, size, size, { color = color })
  draw.image(face, x - size / 2, y - size / 2, size, size)
end

on("world", function()
  map = nil
  incoming = nil
  open = false
  waiting = false
  others = {}
  sentView = nil
  control(false)
  server.send("worldmap:hello", "")
end)

on("message", function(name, data)
  if name == "worldmap:style" then
    local newKey, color = data:match("^(%S+) (%x+)$")
    if not newKey then return end
    if input.consume and newKey ~= key then
      input.consume(key, false)
      input.consume(newKey)
    end
    key, accent = newKey, "#" .. color
    hint = makeHint()
  elseif name == "worldmap:me" then
    local face, ring = data:match("^(%S+) (%S+)$")
    head = face and { face = face, ring = ring } or nil
  elseif name == "worldmap:wait" then
    if data == "1" and not waiting then waitingSince = clock end
    waiting = data == "1"
    if data == "0" then notice, noticeUntil = "This world has nothing to show on a map", clock + 3 end
  elseif name == "worldmap:players" then
    receive(data)
  elseif name == "worldmap:map" then
    local overview, minX, minZ, unit, width, height, opened, background, tileWorld, tiles, saved =
      data:match("^(%S+) (%S+) (%S+) (%S+) (%S+) (%S+) (%S+) (%x+) (%S+) (%S+) (%S+)$")
    if not overview then return end
    local new = {
      file = overview,
      minX = tonumber(minX),
      minZ = tonumber(minZ),
      unit = tonumber(unit),
      width = tonumber(width),
      height = tonumber(height),
      background = "#" .. background,
      tileWorld = tonumber(tileWorld),
      tiles = {},
      saved = file(saved),
    }
    for col, row, id in tiles:gmatch("(%-?%d+):(%-?%d+):(%x+)") do
      new.tiles[#new.tiles + 1] = { tonumber(col), tonumber(row), id }
    end
    if server.file then server.file(overview) end
    if map and open and map.file ~= overview and not ready(overview) then
      incoming = new
    else
      incoming = nil
      switch(new)
    end
    if (opened == "1" or waiting) and not open then toggle(true) end
  end
end)

on("key", function(name, down)
  if not down then return end
  if name == key then
    if input.down("Alt") then return end
    if open then
      toggle(false)
    elseif waiting then
      waiting = false
      server.send("worldmap:open", "0")
    elseif map then
      toggle(true)
    else
      waiting = true
      waitingSince = clock
      server.send("worldmap:open", "1")
    end
  elseif not open or not map then
    return
  elseif name == "Escape" or name == "MouseLeft" and over(closeButton()) then
    toggle(false)
  elseif name == "MouseLeft" and over(findButton()) then
    local x, y = playerPoint()
    if x then centerX, centerY = x, y end
    clampCenter()
  elseif name == "MouseLeft" then
    dragX, dragY = input.mouse()
  end
end)

on("update", function(seconds)
  clock = clock + seconds
  if waiting and clock - waitingSince > waitLimit then
    waiting = false
    server.send("worldmap:open", "0")
    notice, noticeUntil = "The map did not come from the server, try F4 again later", clock + 4
  end
  if incoming and ready(incoming.file) then
    switch(incoming)
    incoming = nil
  end
  if not open or not map then return end
  local mouseX, mouseY = input.mouse()
  if input.wheel and mouseX then
    local wheel = input.wheel()
    if wheel and wheel ~= 0 then zoomAt(zoomStep ^ wheel, mouseX, mouseY) end
  end
  if dragX and mouseX and input.down("MouseLeft") then
    local _, _, scale = view()
    centerX = centerX - (mouseX - dragX) / scale
    centerY = centerY - (mouseY - dragY) / scale
    dragX, dragY = mouseX, mouseY
  else
    dragX, dragY = nil, nil
  end
  clampCenter()
end)

every(0.5, function()
  if open and map then remember() end
end)

on("draw", function()
  local screenWidth, screenHeight = screen.size()
  if waiting or clock < noticeUntil then
    draw.text(waiting and "Drawing the map, it will open in a moment" or notice, screenWidth / 2, screenHeight * 0.3,
      { size = 22, align = "center", font = boldFont })
  end
  if not open or not map then return end
  local _, _, scale = view()
  draw.rect(0, 0, screenWidth, screenHeight, map.background)
  local x = screenWidth / 2 - centerX * scale
  local y = screenHeight / 2 - centerY * scale
  draw.image(map.file, x, y, map.width * scale, map.height * scale, { filter = "nearest" })
  if scale > 1 and #map.tiles > 0 then
    local size = map.tileWorld / map.unit * scale
    local filter = size > 256 and "nearest" or nil
    for _, tile in ipairs(map.tiles) do
      local tileX = x + (tile[1] * map.tileWorld - map.minX) / map.unit * scale
      local tileY = y + (map.height - (-tile[2] * map.tileWorld - map.minZ) / map.unit) * scale
      if tileX < screenWidth and tileY < screenHeight and tileX + size > 0 and tileY + size > 0 then
        draw.image(tile[3], tileX, tileY, size, size, { filter = filter })
      end
    end
  end
  local mouseX, mouseY = input.mouse()
  local hovered
  for _, other in pairs(others) do
    local otherX, otherY = mapPoint(shown(other))
    otherX, otherY = x + otherX * scale, y + otherY * scale
    marker(other.face, other.ring, otherSize, teamColors[other.team] or "#ffffff", otherX, otherY)
    if mouseX and math.abs(mouseX - otherX) <= otherSize / 2 and math.abs(mouseY - otherY) <= otherSize / 2 then
      hovered = { name = other.name, x = otherX, y = otherY - otherSize / 2 - 28 }
    end
  end
  local px, py = playerPoint()
  if px then
    marker(head and head.face, head and head.ring, headSize, teamColors[myTeam] or "#ffffff", x + px * scale, y + py * scale)
  end
  if hovered then
    draw.text(hovered.name, hovered.x, hovered.y, { size = 20, align = "center", font = boldFont })
  end
  if not ready(map.file) then
    draw.text("Loading the map...", screenWidth / 2, screenHeight / 2 - 12, { size = 22, align = "center", font = boldFont })
  end
  button(cross, closeButton())
  local findX, findY = findButton()
  if button(target, findX, findY) then
    draw.text("Find yourself", findX + buttonSize / 2, findY + buttonSize + 6, { size = 16, align = "center", font = font })
  end
  line(hint, screenWidth / 2, screenHeight - 40, 18)
end)
