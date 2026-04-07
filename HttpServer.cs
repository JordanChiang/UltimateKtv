using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UltimateKtv;

namespace UltimateKtv;

public class HttpServer : IDisposable
{
	public object? MainDataContext;
	public object? MainConfigData;
	public object? MainSongData;

	private IWebHost? _webHost;
	private readonly string _ipAddress;
	private readonly int _port;

	// SSE (Server-Sent Events) support for real-time notifications
	private static readonly List<HttpResponse> _sseClients = new List<HttpResponse>();
	private static readonly object _sseClientsLock = new object();

	// Singleton instance for broadcasting events
	public static HttpServer? Instance { get; private set; }

	// Cached singer data table (sorted by strokes)
	public static DataTable? CachedSingerTable { get; private set; }

	/// <summary>
	/// Gets the server URL for external access
	/// </summary>
	public string ServerUrl
	{
		get
		{
			string localIp = GetLocalIPAddress();
			return $"http://{(_ipAddress == "0.0.0.0" || _ipAddress == "127.0.0.1" ? localIp : _ipAddress)}:{_port}";
		}
	}

	public HttpServer(string ipAddress, int port)
	{
		_ipAddress = ipAddress;
		_port = port;
		Instance = this;
	}

	/// <summary>
	/// Broadcasts an event to all connected SSE clients
	/// </summary>
	public static void BroadcastEvent(string eventType, object? data = null)
	{
		var message = $"event: {eventType}\ndata: {JsonConvert.SerializeObject(data ?? new { })}\n\n";
		var bytes = Encoding.UTF8.GetBytes(message);

		lock (_sseClientsLock)
		{
			var disconnectedClients = new List<HttpResponse>();
			foreach (var client in _sseClients)
			{
				try
				{
					client.Body.WriteAsync(bytes, 0, bytes.Length);
					client.Body.FlushAsync();
				}
				catch
				{
					disconnectedClients.Add(client);
				}
			}
			// Remove disconnected clients
			foreach (var client in disconnectedClients)
			{
				_sseClients.Remove(client);
			}
		}
		
		if(eventType != "YoutubeProgress")
			System.Diagnostics.Debug.WriteLine($"[SSE] Broadcast '{eventType}' to {_sseClients.Count} clients");
	}

	public void Start()
	{
		_webHost = new WebHostBuilder()
			.UseKestrel(options =>
			{
				options.Listen(System.Net.IPAddress.Parse(_ipAddress), _port);
			})
			.Configure(app =>
			{
				app.Run(async context =>
				{
					await HandleRequest(context);
				});
			})
			.Build();

		_webHost.Start();
	}

	public void Stop()
	{
		if (_webHost == null) return;
		try
		{
			// Clear SSE clients first to prevent new writes during shutdown
			lock (_sseClientsLock)
			{
				_sseClients.Clear();
			}

			// Try to stop gracefully for 2 seconds, then force stop
			using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
			{
				_webHost.StopAsync(cts.Token).GetAwaiter().GetResult();
				System.Diagnostics.Debug.WriteLine($"_webHost.StopAsync success");
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[HttpServer] Error stopping web host: {ex.Message}");
		}
		finally
		{
			// IMPORTANT: Dispose releases the socket/port binding
			_webHost?.Dispose();
			_webHost = null;
			Instance = null;
		}
	}

	private async Task HandleRequest(HttpContext context)
	{
		try
		{
			var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
			var path = context.Request.Path.Value;
			var query = context.Request.QueryString.Value;
            
            // Only log non-image requests to reduce noise and overhead
            if (path == null || !path.Contains("singerphoto"))
            {
			    System.Diagnostics.Debug.WriteLine($"[{timestamp}] HTTP {context.Request.Method} {path}{query}");
            }
			
			if (context.Request.Method == "POST")
			{
				await HandlePost(context);
			}
			else if (context.Request.Method == "GET")
			{
				await HandleGet(context);
			}
			else
			{
				context.Response.StatusCode = 405;
				await context.Response.WriteAsync("Method Not Allowed");
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[HTTP ERROR] {ex.Message}");
			System.Diagnostics.Debug.WriteLine($"[HTTP ERROR] StackTrace: {ex.StackTrace}");
			context.Response.StatusCode = 500;
			await context.Response.WriteAsync($"Internal Server Error: {ex.Message}");
		}
	}

	private async Task HandlePost(HttpContext context)
	{
		var form = await context.Request.ReadFormAsync();
		var paramsStr = string.Join(";", form.Select(x => $"{x.Key}={x.Value}"));
		
		System.Diagnostics.Debug.WriteLine($"[HTTP POST] {paramsStr}");
		string responseText = $"OnPost: {paramsStr}";

		context.Response.StatusCode = 200;
		context.Response.ContentType = "text/html; charset=UTF-8";
		context.Response.Headers["Server"] = "UltimateKTV-Remote-Server";
		
		await context.Response.WriteAsync(responseText, Encoding.UTF8);
	}

	private async Task HandleGet(HttpContext context)
	{
		string url = context.Request.Path.Value ?? "";
		var queryParams = context.Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString());
		string queryType = queryParams.ContainsKey("queryType") ? queryParams["queryType"] : "";

		// Handle SSE endpoint for real-time notifications
		if (queryType == "events")
		{
			await HandleSSE(context);
			return;
		}

		// If no query parameters, show welcome page
		if (queryParams.Count == 0)
		{
			await ShowWelcomePage(context);
			return;
		}

		// Check if this is a valid API query
		bool isValidQuery = !string.IsNullOrEmpty(queryType) && IsValidQueryType(queryType);

		string responseJson;

		if (!isValidQuery)
		{
			responseJson = JsonConvert.SerializeObject(new JArray());
		}
		else
		{
			string lang = queryParams.ContainsKey("lang") ? queryParams["lang"] : string.Empty;
			string singer = queryParams.ContainsKey("singer") ? queryParams["singer"] : string.Empty;
			string words = queryParams.ContainsKey("words") ? queryParams["words"] : string.Empty;
			string condition = queryParams.ContainsKey("condition") ? queryParams["condition"] : string.Empty;
			string page = queryParams.ContainsKey("page") ? queryParams["page"] : string.Empty;
			string rows = queryParams.ContainsKey("rows") ? queryParams["rows"] : string.Empty;
			string sort = queryParams.ContainsKey("sort") ? queryParams["sort"] : string.Empty;
			string user = queryParams.ContainsKey("user") ? queryParams["user"] : string.Empty;
			string value = queryParams.ContainsKey("value") ? queryParams["value"] : string.Empty;
			string state = queryParams.ContainsKey("state") ? queryParams["state"] : string.Empty;

			// Log user parameter for debugging
			if (!string.IsNullOrEmpty(user))
			{
				System.Diagnostics.Debug.WriteLine($"[HTTP] User parameter: {user}");
			}

			// Debug logging - skip for high frequency singerphoto query types
            if (queryType != "singerphoto")
            {
			    System.Diagnostics.Debug.WriteLine($"[HTTP] Query: {queryType}, Lang: {lang}, Singer: {singer}, Words: {words}");
			    System.Diagnostics.Debug.WriteLine($"[HTTP] SongData loaded: {SongDatas.SongData?.Count ?? 0} songs");
            }

			responseJson = queryType switch
			{
				"querysong" => HttpServerHelper.QuerySong(lang, singer, words, condition, page, rows, sort),
				"querynewsong" => HttpServerHelper.QueryNewSong(lang, page, rows),
				"querysinger" => HttpServerHelper.QuerySinger(condition, page, rows, sort, MainSongData!, MainDataContext!),
				"viewsong" => HttpServerHelper.ViewSong(page, rows, MainSongData!),
				"favoriteuser" => HttpServerHelper.FavoriteUser(page, rows, MainSongData!),
				"favoritelogin" => HttpServerHelper.FavoriteLogin(user, MainSongData!),
				"favoritesong" => HttpServerHelper.FavoriteSong(user, page, rows, MainSongData!, MainSongData!),
				"addfavoritesong" => HttpServerHelper.AddFavoriteSong(user, value, MainSongData!, MainDataContext!),
				"ordersong" => HttpServerHelper.OrderSong(value, user, MainSongData!, MainSongData!, MainDataContext!),
				"queryplayerstate" => HttpServerHelper.QueryPlayerState(value, MainDataContext!),
				"queryhostserverinfo" => HttpServerHelper.QueryHostServerInfo(value, MainDataContext!),
				"queryplaylist" => HttpServerHelper.QueryPlaylist(value, MainDataContext!),
				"querygeneration" => HttpServerHelper.QueryGeneration(value, page, rows),
				"skipsong" => HttpServerHelper.SkipCurrentSong(value, MainDataContext!),
				"vocal" => HttpServerHelper.SwitchToVocal(value, MainDataContext!),
				"music" => HttpServerHelper.SwitchToMusic(value, MainDataContext!),
                "movetofirst" => HttpServerHelper.MoveToFirst(value, MainDataContext!),
                "removefromlist" => HttpServerHelper.RemoveFromList(value, MainDataContext!),
                "movetonext" => HttpServerHelper.MoveToNext(value, MainDataContext!),
                "volup" => HttpServerHelper.VolumeUp(value, MainDataContext!),
                "voldown" => HttpServerHelper.VolumeDown(value, MainDataContext!),
                "vollock" => HttpServerHelper.ToggleVolumeLock(value, MainDataContext!),
                "pitchup" => HttpServerHelper.PitchUp(value, MainDataContext!),
                "pitchdown" => HttpServerHelper.PitchDown(value, MainDataContext!),
                "pitchreset" => HttpServerHelper.PitchReset(value, MainDataContext!),
                "quit" => HttpServerHelper.QuitApp(value, MainDataContext!),
				"debug" => GetDebugInfo(),
                "singerphoto" => await HandleSingerPhoto(context, queryParams),
                "youtubesearch" => await HttpServerHelper.SearchYoutubeAsync(value),
                "orderyoutube" => await HttpServerHelper.OrderYoutubeAsync(value, user, MainDataContext!),
				_ => JsonConvert.SerializeObject(new JArray())
			};
			
            if (queryType != "singerphoto")
            {
			    System.Diagnostics.Debug.WriteLine($"[HTTP] Response length: {responseJson.Length}");
            }
		}

        // If it was an image request, the response was already handled by HandleSingerPhoto
        if (queryType == "singerphoto") return;

		context.Response.StatusCode = 200;
		context.Response.ContentType = "application/json; charset=UTF-8";
		context.Response.Headers["Server"] = "UltimateKTV-Remote-Server";
		
		await context.Response.WriteAsync(responseJson, Encoding.UTF8);
	}

	private bool IsValidQueryType(string queryType)
	{
		return queryType switch
		{
			"querysong" or "querynewsong" or "querysinger" or "viewsong" or "favoriteuser" or
			"favoritelogin" or "favoritesong" or "addfavoritesong" or "ordersong" or
			"queryplayerstate" or "queryhostserverinfo" or "queryplaylist" or "querygeneration" or "skipsong" or
			"vocal" or "music" or
            "movetofirst" or "removefromlist" or "movetonext" or
            "volup" or "voldown" or "vollock" or
            "pitchup" or "pitchdown" or "pitchreset" or "quit" or
			"debug" or "events" or "singerphoto" or "youtubesearch" or "orderyoutube" => true,
			_ => false
		};
	}

	/// <summary>
	/// Handles Server-Sent Events (SSE) connection for real-time notifications
	/// </summary>
	private async Task HandleSSE(HttpContext context)
	{
		context.Response.Headers["Content-Type"] = "text/event-stream";
		context.Response.Headers["Cache-Control"] = "no-cache";
		context.Response.Headers["Connection"] = "keep-alive";
		context.Response.Headers["Access-Control-Allow-Origin"] = "*";

		// Send initial connection message
		var connectMsg = "event: connected\ndata: {\"status\":\"connected\"}\n\n";
		await context.Response.WriteAsync(connectMsg, Encoding.UTF8);
		await context.Response.Body.FlushAsync();

		// Add to SSE clients list
		lock (_sseClientsLock)
		{
			_sseClients.Add(context.Response);
		}
		System.Diagnostics.Debug.WriteLine($"[SSE] Client connected. Total clients: {_sseClients.Count}");

		// Keep connection alive until client disconnects
		try
		{
			while (!context.RequestAborted.IsCancellationRequested)
			{
				await Task.Delay(30000, context.RequestAborted); // Send keepalive every 30s
				await context.Response.WriteAsync(": keepalive\n\n", Encoding.UTF8);
				await context.Response.Body.FlushAsync();
			}
		}
		catch (OperationCanceledException)
		{
			// Client disconnected
		}
		finally
		{
			lock (_sseClientsLock)
			{
				_sseClients.Remove(context.Response);
			}
			System.Diagnostics.Debug.WriteLine($"[SSE] Client disconnected. Total clients: {_sseClients.Count}");
		}
	}

	private async Task ShowWelcomePage(HttpContext context)
	{
		// Use application directory instead of current working directory
		string appDir = AppDomain.CurrentDomain.BaseDirectory;
		string htmlPath = Path.Combine(appDir, "wwwroot", "index.html");
		
		if (!File.Exists(htmlPath))
		{
			context.Response.StatusCode = 500;
			await context.Response.WriteAsync($"Error: index.html not found at {htmlPath}", Encoding.UTF8);
			return;
		}
		
		var html = File.ReadAllText(htmlPath);
		context.Response.StatusCode = 200;
		context.Response.ContentType = "text/html; charset=UTF-8";
		context.Response.Headers["Server"] = "UltimateKTV-Remote-Server";
		await context.Response.WriteAsync(html, Encoding.UTF8);
	}

	private string GetDebugInfo()
	{
		var settings = SettingsManager.Instance.CurrentSettings;
		var info = new JObject
		{
			{ "SongDataCount", SongDatas.SongData?.Count ?? 0 },
			{ "SingerDataCount", SongDatas.SingerData?.Count ?? 0 },
			{ "PlayListDataCount", SongDatas.PlayListData?.Rows.Count ?? 0 },
			{ "FavoriteUserDataCount", SongDatas.FavoriteUserData?.Rows.Count ?? 0 },
			{ "FavoriteSongDataCount", SongDatas.FavoriteSongData?.Count ?? 0 },
			{ "SongLangList", SongDatas.SongLangList != null ? string.Join(", ", SongDatas.SongLangList) : "null" },
			{ "NetworkRemoteSongUsername", settings.NetworkRemoteSongUsername },
			{ "WebDefaultNumOrder", settings.WebDefaultNumOrder },
            { "VisualSingerStyle", settings.VisualSingerStyle },
            { "IsVisualSingerStyleEffective", SingerPhotoManager.HasAnyPhotos() && settings.VisualSingerStyle }
		};
		return JsonConvert.SerializeObject(info, Formatting.Indented);
	}

    private async Task<string> HandleSingerPhoto(HttpContext context, Dictionary<string, string> queryParams)
    {
        string name = queryParams.ContainsKey("name") ? queryParams["name"] : string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            context.Response.StatusCode = 400;
            return string.Empty;
        }

        string? localPath = SingerPhotoManager.GetPhotoLocalPath(name);
        if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
        {
            context.Response.StatusCode = 404;
            return string.Empty;
        }

        try
        {
            string ext = Path.GetExtension(localPath).ToLower();
            string contentType = HttpServerHelper.MimeTypeMap.ContainsKey(ext) 
                ? HttpServerHelper.MimeTypeMap[ext] 
                : "image/jpeg";

            context.Response.StatusCode = 200;
            context.Response.ContentType = contentType;
            
            // Serve file directly to response body asynchronously
            using (var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
            {
                await fs.CopyToAsync(context.Response.Body);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HttpServer] Error serving singer photo: {ex.Message}");
            context.Response.StatusCode = 500;
        }

        return string.Empty; // Response already handled
    }

	public void Dispose()
	{
		Stop();  // Stop() now handles dispose internally
	}

	/// <summary>
	/// Starts the HTTP server for remote control
	/// </summary>
	public static HttpServer? StartHttpServer(MainWindow mainWindow, Action<string> debugLog)
	{
		try
		{
			var settings = SettingsManager.Instance.CurrentSettings;
			
			if (!settings.EnableHttpServer)
			{
				AppLogger.Log("HTTP server is disabled in settings.");
				return null;
			}

			string ipAddress = settings.HttpServerIp ?? "0.0.0.0";
			int port = settings.HttpServerPort;

			// Find available port if the configured one is in use
			if (!IsPortAvailable(port))
			{
				AppLogger.Log($"Port {port} is in use, finding alternative...");
				port = HttpServerHelper.GetAvailablePort(8080, 8099, System.Net.IPAddress.Parse("0.0.0.0"), false);
				if (port == 0)
				{
					AppLogger.LogError("No available ports found for HTTP server.");
					return null;
				}
				AppLogger.Log($"Using alternative port: {port}");
			}

			debugLog($"Creating HTTP server on {ipAddress}:{port}");
			var httpServer = new HttpServer(ipAddress, port);
			httpServer.MainDataContext = mainWindow;
			httpServer.MainSongData = SongDatas.PlayListData;
			
			debugLog("Starting HTTP server...");
			httpServer.Start();
			debugLog("HTTP server started successfully");

			string localIp = GetLocalIPAddress();
			string serverUrl = $"http://{(ipAddress == "0.0.0.0" ? localIp : ipAddress)}:{port}";
			string startupMessage = $"遠端伺服器已啟動: {serverUrl}";
			
			AppLogger.Log(startupMessage);
			debugLog(startupMessage);
			debugLog($"Server listening on: http://{ipAddress}:{port}");
			debugLog($"Access from browser: {serverUrl}");
			debugLog($"SongData count: {SongDatas.SongData?.Count ?? 0}");

			// Prepare cached singer data for faster and consistent sorting
			PrepareCachedSingerData();

			return httpServer;
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Failed to start HTTP server", ex);
			debugLog($"HTTP server error: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Checks if a port is available
	/// </summary>
	private static bool IsPortAvailable(int port)
	{
		try
		{
			var usedPort = HttpServerHelper.GetAvailablePort(port, port, System.Net.IPAddress.Parse("0.0.0.0"), false);
			return usedPort == port;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Gets the local IP address
	/// </summary>
	public static string GetLocalIPAddress()
	{
		try
		{
			string localIP = "127.0.0.1";
			foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
			{
				// Pick the first valid IPv4 that's not loopback or virtual
				if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
					// !ip.ToString().StartsWith("10.") &&		// skip huge local subnet
					!ip.ToString().StartsWith("169.") &&  // skip link-local
					!ip.ToString().StartsWith("127.") &&  // skip loopback
					!ip.ToString().StartsWith("192.168.56.") && // skip VirtualBox
					!ip.ToString().StartsWith("172.16.")  // optional: skip Docker subnet
					)
				{
					localIP = ip.ToString();
					break;
				}
			}
			return localIP;
		}
		catch
		{
			return "127.0.0.1";
		}
	}

	/// <summary>
	/// Gets the public IP address by querying external services
	/// </summary>
	private static string? _cachedPublicIP;

	/// <summary>
	/// Gets the public IP address by querying external services asynchronously
	/// </summary>
	public static async Task<string> GetPublicIPAddressAsync()
	{
		if (!string.IsNullOrEmpty(_cachedPublicIP))
		{
			return _cachedPublicIP;
		}

		try
		{
			using (var client = new System.Net.Http.HttpClient())
			{
				client.Timeout = TimeSpan.FromSeconds(5);
				// Try multiple services in case one is down
				string[] services = {
					"https://api.ipify.org",
					"https://icanhazip.com",
					"https://checkip.amazonaws.com"
				};
				
				foreach (var service in services)
				{
					try
					{
						var response = await client.GetStringAsync(service);
						var ip = response.Trim();
						if (!string.IsNullOrEmpty(ip) && System.Net.IPAddress.TryParse(ip, out _))
						{
							_cachedPublicIP = ip;
							return ip;
						}
					}
					catch
					{
						continue;
					}
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Failed to get public IP address", ex);
		}
		return "Unable to get public IP";
	}

	/// <summary>
	/// Gets the public IP address by querying external services
	/// </summary>
	public static string GetPublicIPAddress()
	{
		if (!string.IsNullOrEmpty(_cachedPublicIP))
		{
			return _cachedPublicIP;
		}

		try
		{
			// For backward compatibility, run the async method synchronously
			// Note: This might still block, but it's better to use the async version everywhere
			return Task.Run(async () => await GetPublicIPAddressAsync()).Result;
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Failed to get public IP address synchronously", ex);
		}
		return "Unable to get public IP";
	}

	/// <summary>
	/// Prepares the cached singer data table with numeric sorting for strokes
	/// </summary>
	public static void PrepareCachedSingerData()
	{
		if (SongDatas.SingerData == null)
        {
             System.Diagnostics.Debug.WriteLine("[HttpServer] PrepareCachedSingerData failed: SongDatas.SingerData is null.");
             return;
        }
        if (SongDatas.SingerDataSchema == null)
        {
             System.Diagnostics.Debug.WriteLine("[HttpServer] PrepareCachedSingerData failed: SongDatas.SingerDataSchema is null.");
             return;
        }

		try
		{
			// Clone structure from schema
			var dt = SongDatas.SingerDataSchema.Clone();
			
            // Populate data from the globally SORTED list
            foreach (var singerDictionary in SongDatas.SingerData)
			{
				var row = dt.NewRow();
				foreach (DataColumn col in dt.Columns)
				{
					if (singerDictionary.ContainsKey(col.ColumnName))
					{
						row[col.ColumnName] = singerDictionary[col.ColumnName] ?? DBNull.Value;
					}
				}
				dt.Rows.Add(row);
			}

			CachedSingerTable = dt; // Use the table directly as it preserves insertion order
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[HttpServer] Failed to prepare cached singer data: {ex.Message}");
		}
	}
}
