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
	/// <summary>手機指令對主程式的操作 (切到 UI 執行緒)；伺服器啟動前設定。</summary>
	public RemoteControl Remote = null!;

	private IWebHost? _webHost;
	private readonly string _ipAddress;
	private readonly int _port;
	private readonly Dictionary<string, ApiEndpoint> _api;

	// SSE (Server-Sent Events) support for real-time notifications
	private static readonly List<HttpResponse> _sseClients = new List<HttpResponse>();
	private static readonly object _sseClientsLock = new object();

	// Singleton instance for broadcasting events
	public static HttpServer? Instance { get; private set; }

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
		_api = CreateApi();
	}

	/// <summary>
	/// The port actually bound by the running server (may differ from settings if the configured port was in use)
	/// </summary>
	public int Port => _port;

	/// <summary>
	/// The port to show to users: the running server's port, or the configured port if no server is running
	/// </summary>
	public static int CurrentPort => Instance?.Port ?? SettingsManager.Instance.CurrentSettings.HttpServerPort;

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

		try
		{
			_webHost.Start();
			Instance = this;  // Only publish once the port is actually bound
		}
		catch
		{
			// Release the partially-built host so a retry can bind cleanly
			_webHost.Dispose();
			_webHost = null;
			throw;
		}
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
            
            bool isSingerPhoto = (path != null && path.Contains("singerphoto")) || (query != null && query.Contains("singerphoto"));
            bool isEvents = query != null && query.Contains("queryType=events");

            if (!isSingerPhoto)
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

            if (context.Response.StatusCode >= 400 && !isEvents && !isSingerPhoto)
            {
                AppLogger.Log($"[Network] Failed Request: {context.Request.Method} {path}{query} - Status: {context.Response.StatusCode}");
            }
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[HTTP ERROR] {ex.Message}");
			System.Diagnostics.Debug.WriteLine($"[HTTP ERROR] StackTrace: {ex.StackTrace}");
			AppLogger.LogError($"[Network] HTTP ERROR during {context.Request.Method} {context.Request.Path.Value}", ex);
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

		// 網頁文字與回應訊息依裝置語言 (瀏覽器的 Accept-Language)；只在這個請求的非同步流程內有效
		Loc.WebCulture = Loc.ResolveWebCulture(context.Request.Headers["Accept-Language"].ToString());

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

		string responseJson;

		if (!_api.TryGetValue(queryType, out var endpoint))
		{
			responseJson = JsonConvert.SerializeObject(new JArray());
		}
		else
		{
			var request = new ApiRequest(context, queryParams);

			// Log user parameter for debugging
			if (!string.IsNullOrEmpty(request.User))
			{
				System.Diagnostics.Debug.WriteLine($"[HTTP] User parameter: {request.User}");
			}

			// Debug logging - skip for high frequency singerphoto query types
            if (queryType != "singerphoto")
            {
			    System.Diagnostics.Debug.WriteLine($"[HTTP] Query: {queryType}, Lang: {request.Lang}, Singer: {request.Singer}");
			    System.Diagnostics.Debug.WriteLine($"[HTTP] SongData loaded: {SongDatas.SongData?.Count ?? 0} songs");
                
                // Only log actionable or search queries to AppLogger (filter out periodic polling)
                if (!endpoint.Silent) 
                {
                    string userLog = string.IsNullOrEmpty(request.User) ? "" : $"User: {request.User}, ";
                    AppLogger.Log($"[Network API] Action: {queryType}, {userLog}Value: {request.Value}");
                }
            }

			responseJson = await endpoint.Handle(request);
			
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

	/// <summary>一個手機 API (queryType)。Silent = 網頁定時輪詢的查詢，不寫進 AppLogger。</summary>
	private sealed record ApiEndpoint(bool Silent, Func<ApiRequest, Task<string>> Handle);

	/// <summary>手機 API 請求的參數；沒有帶的參數為空字串。</summary>
	private sealed record ApiRequest(HttpContext Context, Dictionary<string, string> Params)
	{
		private string Get(string key) => Params.TryGetValue(key, out var v) ? v : string.Empty;
		public string Value => Get("value");
		public string Lang => Get("lang");
		public string Singer => Get("singer");
		public string Page => Get("page");
		public string Rows => Get("rows");
		public string User => Get("user");
		public string Type => Get("type");
	}

	/// <summary>
	/// 所有手機 API。queryType 不在表中時回傳空陣列；播放控制指令 (切歌、音量…) 來自 <see cref="RemoteCommands"/>。
	/// events (SSE) 在查表前另外處理。
	/// </summary>
	private Dictionary<string, ApiEndpoint> CreateApi()
	{
		static ApiEndpoint Query(Func<ApiRequest, string> handle) => new(false, r => Task.FromResult(handle(r)));
		static ApiEndpoint Poll(Func<ApiRequest, string> handle) => new(true, r => Task.FromResult(handle(r)));

		var api = new Dictionary<string, ApiEndpoint>
		{
			["querysong"] = Query(_ => HttpServerHelper.OutdatedPage()),
			["searchsong"] = Query(r => HttpServerHelper.SearchSongs(r.Value, r.Lang, r.Page, r.Rows)),
			["ranking"] = Query(r => HttpServerHelper.QueryRanking(r.Value, r.Page, r.Rows)),
			["singersong"] = Query(r => HttpServerHelper.QuerySingerSongs(r.Singer, r.Page, r.Rows)),
			["songbyid"] = Query(r => HttpServerHelper.QuerySongById(r.Value)),
			["querynewsong"] = Query(r => HttpServerHelper.QueryNewSong(r.Lang, r.Page, r.Rows)),
			["querysinger"] = Query(_ => HttpServerHelper.OutdatedPage()),
			["singerlist"] = Query(r => HttpServerHelper.QuerySingerList(r.Type, r.Value, r.Page, r.Rows)),
			["favoriteuser"] = Query(r => HttpServerHelper.FavoriteUser(r.Page, r.Rows)),
			["favoritelogin"] = Query(r => HttpServerHelper.FavoriteLogin(r.User)),
			["favoritesong"] = Query(r => HttpServerHelper.FavoriteSong(r.User, r.Page, r.Rows)),
			["ordersong"] = Query(r => HttpServerHelper.OrderSong(r.Value, r.User, Remote)),
			["queryplayerstate"] = Poll(_ => HttpServerHelper.QueryPlayerState(Remote)),
			["queryhostserverinfo"] = Poll(_ => HttpServerHelper.QueryHostServerInfo()),
			["queryplaylist"] = Poll(r => HttpServerHelper.QueryPlaylist(r.Value, Remote)),
			["querygeneration"] = Query(r => HttpServerHelper.QueryGeneration(r.Value, r.Page, r.Rows)),
			["debug"] = Poll(_ => GetDebugInfo()),
			["singerphoto"] = new(true, r => HandleSingerPhoto(r.Context, r.Params)),
			["youtubesearch"] = new(false, r => HttpServerHelper.SearchYoutubeAsync(r.Value)),
			["orderyoutube"] = new(false, r => HttpServerHelper.OrderYoutubeAsync(r.Value, r.User, Remote)),
			["orderyoutubehistory"] = Query(r => HttpServerHelper.OrderYoutubeHistory(r.Value, r.User, Remote)),
			["queryyoutubehistory"] = Poll(r => HttpServerHelper.QueryYoutubeHistory(r.Page, r.Rows)),
			["checknewyoutube"] = new(true, _ => HttpServerHelper.CheckNewYoutubeAsync(Remote)),
			["importnewyoutube"] = new(false, _ => HttpServerHelper.ImportNewYoutubeAsync(Remote)),
			["queryyoutubequeue"] = Poll(_ => HttpServerHelper.QueryYoutubeQueue(Remote)),
			["uistrings"] = Poll(_ => JsonConvert.SerializeObject(new { culture = Loc.WebCulture ?? Loc.CurrentCulture, strings = Loc.GetWebStrings() })),
		};

		foreach (var name in RemoteCommands.Names)
		{
			api.Add(name, Query(r =>
			{
				RemoteCommands.TryRun(name, r.Value, Remote, key => Loc.T(key), out var json);
				return json;
			}));
		}
		return api;
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
		AppLogger.Log($"[Network] SSE Client connected. Total clients: {_sseClients.Count}");

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
			AppLogger.Log($"[Network] SSE Client disconnected. Total clients: {_sseClients.Count}");
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
			{ "WaitingListCount", Remote.GetPlayerState().WaitingCount },
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

			// Try the configured port first, retrying briefly in case a previous instance
			// is still shutting down. Only fall back to another port if it stays occupied.
			var httpServer = TryStartOnPort(mainWindow, ipAddress, port, PortBindRetryCount, debugLog);
			if (httpServer == null)
			{
				AppLogger.Log($"Port {port} is in use, finding alternative...");
				for (int candidate = 8080; candidate <= 8099 && httpServer == null; candidate++)
				{
					if (candidate == port || !IsPortAvailable(candidate)) continue;
					httpServer = TryStartOnPort(mainWindow, ipAddress, candidate, 1, debugLog);
					if (httpServer != null) port = candidate;
				}
				if (httpServer == null)
				{
					AppLogger.LogError("No available ports found for HTTP server.");
					return null;
				}
				AppLogger.Log($"Using alternative port: {port}");
			}
			debugLog("HTTP server started successfully");

			string localIp = GetLocalIPAddress();
			string serverUrl = $"http://{(ipAddress == "0.0.0.0" ? localIp : ipAddress)}:{port}";
			string startupMessage = Loc.T("Http.Started", serverUrl);
			
			AppLogger.Log(startupMessage);
			debugLog(startupMessage);
			debugLog($"Server listening on: http://{ipAddress}:{port}");
			debugLog($"Access from browser: {serverUrl}");
			debugLog($"SongData count: {SongDatas.SongData?.Count ?? 0}");

			return httpServer;
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Failed to start HTTP server", ex);
			debugLog($"HTTP server error: {ex.Message}");
			return null;
		}
	}

	private const int PortBindRetryCount = 6;
	private const int PortBindRetryDelayMs = 500;

	/// <summary>
	/// Attempts to start the server on the given port, retrying while the address is in use.
	/// Returns null if the port could not be bound after all attempts.
	/// </summary>
	private static HttpServer? TryStartOnPort(MainWindow mainWindow, string ipAddress, int port, int attempts, Action<string> debugLog)
	{
		for (int attempt = 1; attempt <= attempts; attempt++)
		{
			debugLog($"Starting HTTP server on {ipAddress}:{port} (attempt {attempt}/{attempts})...");
			var httpServer = new HttpServer(ipAddress, port);
			httpServer.Remote = new RemoteControl(mainWindow);
			try
			{
				httpServer.Start();
				return httpServer;
			}
			catch (Exception ex) when (IsAddressInUse(ex))
			{
				debugLog($"Port {port} is in use: {ex.Message}");
				if (attempt < attempts)
					Thread.Sleep(PortBindRetryDelayMs);
			}
		}
		return null;
	}

	/// <summary>
	/// Kestrel wraps bind failures (IOException / AddressInUseException); look for the socket error underneath.
	/// </summary>
	private static bool IsAddressInUse(Exception ex)
	{
		for (Exception? e = ex; e != null; e = e.InnerException)
		{
			if (e is System.Net.Sockets.SocketException se && se.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
				return true;
			if (e.GetType().Name == "AddressInUseException")
				return true;
		}
		return false;
	}

	/// <summary>
	/// Checks if a port is available (TIME_WAIT connections are ignored since they don't block a new listener)
	/// </summary>
	private static bool IsPortAvailable(int port)
	{
		try
		{
			var usedPort = HttpServerHelper.GetAvailablePort(port, port, System.Net.IPAddress.Parse("0.0.0.0"), true);
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
			foreach (var item in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
			{
				if (item.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
				{
					foreach (var ip in item.GetIPProperties().UnicastAddresses)
					{
						if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
							!ip.Address.ToString().StartsWith("169.") &&
							!ip.Address.ToString().StartsWith("127.") &&
							!ip.Address.ToString().StartsWith("192.168.56.") &&
							!ip.Address.ToString().StartsWith("172.16."))
						{
							return ip.Address.ToString();
						}
					}
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

}
