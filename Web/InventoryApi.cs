using System.Globalization;
using System.Text.Json;
using OpenKogama.Game;
using OpenKogama.Storage;

namespace OpenKogama.Web;

public static class InventoryApi
{
    public static ApiResponse Route(string path, ApiRequest request, int profile)
    {
        bool post = request.Method == "POST";
        int id = int.TryParse(request.Query["id"], out int given) ? given : 0;
        return path switch
        {
            "/api/inventory" => ApiResponse.Json(JsonSerializer.Serialize(
                Stores.Profiles.Items(profile).Select(item => new { id = item.Id, name = item.Name, category = item.Category }))),
            "/api/inventory/model" => ModelFiles.Export(profile, id) is byte[] file
                ? new ApiResponse(200, "application/octet-stream", file)
                : ApiResponse.NotFound,
            "/api/inventory/import" when post => ModelFiles.Import(profile, request.Query["name"], request.Body, Scale(request)) is Item item
                ? ApiResponse.Json(JsonSerializer.Serialize(new { id = item.Id }))
                : ApiResponse.Json("{}", 400),
            "/api/inventory/scale" when post => ApiResponse.Json("{}",
                Scale(request) is float scale && ModelFiles.Rescale(profile, id, scale) ? 200 : 400),
            "/api/inventory/rename" when post => ApiResponse.Json("{}",
                ModelFiles.CleanName(request.Query["name"]) is string name && Stores.Profiles.RenameItem(profile, id, name) ? 200 : 400),
            "/api/inventory/delete" when post => ApiResponse.Json("{}", Inventories.Remove(profile, id) ? 200 : 404),
            _ => ApiResponse.NotFound,
        };
    }

    static float? Scale(ApiRequest request) =>
        float.TryParse(request.Query["scale"], NumberStyles.Float, CultureInfo.InvariantCulture, out float scale) ? scale : null;
}
