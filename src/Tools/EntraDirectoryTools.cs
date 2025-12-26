using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using Azure.Identity;
using Azure.Core;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace McpServer.Tools;

[McpServerToolType]
public class EntraDirectoryTools
{
    private static GraphServiceClient? _graphClient;
    private static readonly string[] _graphScopes = new[] { "https://graph.microsoft.com/.default" };

    // Configure once at startup (optional)
    public static void Configure()
    {
        var credential = new DefaultAzureCredential();
        _graphClient = new GraphServiceClient(credential, _graphScopes);
    }

    private static GraphServiceClient Client
    {
        get
        {
            if (_graphClient == null)
                Configure();
            return _graphClient!;
        }
    }

    // -----------------------------
    // LOOKUP USER
    // -----------------------------
 [McpServerTool, Description("Lookup a user in Microsoft Entra ID by UPN, email, display name, or object ID.")]
    public static async Task<string> LookupUser(
        [Description("Search text (UPN, email, display name, or object ID).")] string query)
    {
        try
        {
            // Try direct lookup by ID/UPN
            try
            {
                var user = await Client.Users[query].GetAsync();
                if (user != null)
                    return Json(user);
            }
            catch { /* ignore and fall back to search */ }

            // Search by displayName or mail
            var result = await Client.Users.GetAsync(req =>
            {
                req.QueryParameters.Filter =
                    $"startswith(displayName,'{query}') or startswith(mail,'{query}') or startswith(userPrincipalName,'{query}')";
            });

            return Json(result?.Value ?? new List<User>());
        }
        catch (Exception ex)
        {
            return $"Error looking up user: {ex.Message}";
        }
    }

    // -----------------------------
    // LOOKUP GROUP
    // -----------------------------
    [McpServerTool, Description("Lookup a group in Microsoft Entra ID by name or object ID.")]
    public static async Task<string> LookupGroup(
        [Description("Group name or object ID.")] string query)
    {
        try
        {
            // Try direct lookup
            try
            {
                var group = await Client.Groups[query].GetAsync();
                if (group != null)
                    return Json(group);
            }
            catch { }

            // Search by displayName
            var result = await Client.Groups.GetAsync(req =>
            {
                req.QueryParameters.Filter = $"startswith(displayName,'{query}')";
            });

            return Json(result?.Value?.Cast<object>().ToList() ?? new List<object>());
        }
        catch (Exception ex)
        {
            return $"Error looking up group: {ex.Message}";
        }
    }

    // -----------------------------
    // LOOKUP SERVICE PRINCIPAL
    // -----------------------------
    [McpServerTool, Description("Lookup a service principal in Entra ID by name or appId.")]
    public static async Task<string> LookupServicePrincipal(
        [Description("Display name or appId.")] string query)
    {
        try
        {
            var result = await Client.ServicePrincipals.GetAsync(req =>
            {
                req.QueryParameters.Filter =
                    $"startswith(displayName,'{query}') or appId eq '{query}'";
            });

            return Json(result?.Value?.Cast<object>().ToList() ?? new List<object>());
        }
        catch (Exception ex)
        {
            return $"Error looking up service principal: {ex.Message}";
        }
    }

    // -----------------------------
    // LOOKUP APPLICATION
    // -----------------------------
    [McpServerTool, Description("Lookup an Entra application by name or appId.")]
    public static async Task<string> LookupApplication(
        [Description("Display name or appId.")] string query)
    {
        try
        {
            var result = await Client.Applications.GetAsync(req =>
            {
                req.QueryParameters.Filter =
                    $"startswith(displayName,'{query}') or appId eq '{query}'";
            });

            return Json(result?.Value?.Cast<object>().ToList() ?? new List<object>());
        }
        catch (Exception ex)
        {
            return $"Error looking up application: {ex.Message}";
        }
    }

    // -----------------------------
    // GET USER MANAGER
    // -----------------------------
    [McpServerTool, Description("Get the manager of a user in Microsoft Entra ID.")]
    public static async Task<string> GetUserManager(
        [Description("User's email, UPN, or object ID.")] string userQuery)
    {
        try
        {
            // First find the user
            User? user = null;
            
            // Try direct lookup by ID/UPN
            try
            {
                user = await Client.Users[userQuery].GetAsync();
            }
            catch 
            { 
                // Search by displayName or mail
                var searchResult = await Client.Users.GetAsync(req =>
                {
                    req.QueryParameters.Filter =
                        $"startswith(displayName,'{userQuery}') or startswith(mail,'{userQuery}') or startswith(userPrincipalName,'{userQuery}')";
                });
                
                user = searchResult?.Value?.FirstOrDefault();
            }

            if (user == null)
                return $"User '{userQuery}' not found.";

            // Get the manager
            var manager = await Client.Users[user.Id].Manager.GetAsync();
            
            if (manager == null)
                return $"No manager found for user '{user.DisplayName}'.";

            return Json(manager);
        }
        catch (Exception ex)
        {
            return $"Error getting user manager: {ex.Message}";
        }
    }

    // -----------------------------
    // Helper: JSON formatting
    // -----------------------------
    private static string Json(object obj)
    {
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }
}
