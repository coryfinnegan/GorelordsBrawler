#if DEBUG
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GorelordsBrawler.DevTools
{
	/// <summary>
	/// Minimal HTTP server (port 7777) for automated game testing.
	///
	/// GET  /screenshot — returns the current rendered frame as JPEG (blocks until next Draw)
	/// GET  /state      — returns a JSON snapshot of acid + player state
	///
	/// Automation write-channel (all mutate state on the game thread via DebugControl):
	/// POST /input      — { player, moveX?, moveY?, jump?, attack?, special? } scripted input
	/// POST /run        — { mode: "free" | "stepped" } switch the loop's run mode
	/// POST /step       — { frames } advance N fixed-dt frames; blocks until they've run
	/// POST /teleport   — { player, x, y } place a player (setup helper)
	/// POST /damage     — { player, amount } apply raw damage to a player (setup helper)
	/// </summary>
	public static class GameDebugServer
	{
		private static HttpListener _listener;
		private static Thread _thread;
		private static volatile bool _running;

		// Screenshot handshake: HTTP thread sets TCS, game thread completes it in Draw().
		private static TaskCompletionSource<byte[]> _screenshotTcs;
		private static readonly object _screenshotLock = new object();

		// State cache: updated every frame by DebugStateExporter; string is a reference type
		// so reads are always coherent (no torn read of a partial struct).
		private static volatile string _cachedState = "{}";

		public static bool HasPendingScreenshot
		{
			get { lock (_screenshotLock) return _screenshotTcs != null; }
		}

		public static void Start(int port = 7777)
		{
			_listener = new HttpListener();
			_listener.Prefixes.Add($"http://localhost:{port}/");
			try
			{
				_listener.Start();
			}
			catch (Exception e)
			{
				Nez.Debug.Warn($"[DebugServer] Could not start on port {port}: {e.Message}");
				return;
			}

			_running = true;
			_thread = new Thread(ListenLoop) { IsBackground = true, Name = "GameDebugServer" };
			_thread.Start();
			Nez.Debug.Log($"[DebugServer] Listening on http://localhost:{port}/");
		}

		public static void Stop()
		{
			_running = false;
			_listener?.Stop();
		}

		/// <summary>Called by DebugStateExporter once per frame with the serialized game state.</summary>
		public static void UpdateState(string json) => _cachedState = json;

		/// <summary>
		/// Called from the main thread at the end of Draw() when HasPendingScreenshot is true.
		/// Completes the waiting HTTP request with the PNG bytes.
		/// </summary>
		public static void CompleteScreenshot(byte[] pngBytes)
		{
			TaskCompletionSource<byte[]> tcs;
			lock (_screenshotLock)
			{
				tcs = _screenshotTcs;
				_screenshotTcs = null;
			}
			tcs?.TrySetResult(pngBytes);
		}

		// ── Background listener ───────────────────────────────────────────────

		private static void ListenLoop()
		{
			while (_running)
			{
				try
				{
					var ctx = _listener.GetContext();
					_ = Task.Run(() => HandleRequest(ctx));
				}
				catch (HttpListenerException) { break; }
				catch { /* swallow */ }
			}
		}

		private static async Task HandleRequest(HttpListenerContext ctx)
		{
			var req  = ctx.Request;
			var resp = ctx.Response;

			try
			{
				var path = req.Url.AbsolutePath;

				if (req.HttpMethod == "GET" && path == "/screenshot")
				{
					TaskCompletionSource<byte[]> tcs;
					lock (_screenshotLock)
					{
						// Reuse an in-flight request if one is already pending.
						if (_screenshotTcs != null)
							tcs = _screenshotTcs;
						else
							_screenshotTcs = tcs = new TaskCompletionSource<byte[]>();
					}

					byte[] bytes;
					try
					{
						bytes = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
					}
					catch (TimeoutException)
					{
						resp.StatusCode = 408;
						return;
					}

					// GorelordsBrawlerGame.CaptureScreenshot encodes as JPEG —
					// ~5× faster than PNG on the GPU thread so smoke-test
					// recording polling can hit ~30 fps for a smooth clip.
					resp.ContentType = "image/jpeg";
					resp.ContentLength64 = bytes.Length;
					await resp.OutputStream.WriteAsync(bytes);
				}
				else if (req.HttpMethod == "GET" && path == "/state")
				{
					var data = Encoding.UTF8.GetBytes(_cachedState);
					resp.ContentType = "application/json";
					resp.ContentLength64 = data.Length;
					await resp.OutputStream.WriteAsync(data);
				}
				else if (req.HttpMethod == "POST" && path == "/input")
				{
					var body = await ReadJsonBodyAsync(req);
					int player = GetInt(body, "player", 0);
					DebugControl.EnqueueInput(
						player,
						GetNullableInt(body, "moveX"),
						GetNullableInt(body, "moveY"),
						GetNullableBool(body, "jump"),
						GetNullableBool(body, "attack"),
						GetNullableBool(body, "special"));
					await WriteOkAsync(resp);
				}
				else if (req.HttpMethod == "POST" && path == "/run")
				{
					var body = await ReadJsonBodyAsync(req);
					var mode = GetString(body, "mode", "free");
					DebugControl.SetMode(
						string.Equals(mode, "stepped", StringComparison.OrdinalIgnoreCase)
							? RunMode.Stepped
							: RunMode.Free);
					await WriteOkAsync(resp);
				}
				else if (req.HttpMethod == "POST" && path == "/step")
				{
					var body   = await ReadJsonBodyAsync(req);
					int frames = GetInt(body, "frames", 1);

					var stepTask = DebugControl.RequestStepsAsync(frames);
					try
					{
						// Generous ceiling: a step set shouldn't outrun the render loop, but
						// guard against a frozen game so the HTTP client isn't hung forever.
						await stepTask.WaitAsync(TimeSpan.FromSeconds(10));
					}
					catch (TimeoutException)
					{
						resp.StatusCode = 408;
						return;
					}
					await WriteOkAsync(resp);
				}
				else if (req.HttpMethod == "POST" && path == "/teleport")
				{
					var body = await ReadJsonBodyAsync(req);
					DebugControl.EnqueueTeleport(
						GetInt(body, "player", 0),
						(float)GetDouble(body, "x", 0),
						(float)GetDouble(body, "y", 0));
					await WriteOkAsync(resp);
				}
				else if (req.HttpMethod == "POST" && path == "/damage")
				{
					var body = await ReadJsonBodyAsync(req);
					DebugControl.EnqueueDamage(
						GetInt(body, "player", 0),
						GetInt(body, "amount", 0));
					await WriteOkAsync(resp);
				}
				else
				{
					resp.StatusCode = 404;
				}
			}
			catch (Exception e)
			{
				try
				{
					resp.StatusCode = 500;
					var msg = Encoding.UTF8.GetBytes(e.Message);
					resp.ContentLength64 = msg.Length;
					await resp.OutputStream.WriteAsync(msg);
				}
				catch { /* swallow write errors */ }
			}
			finally
			{
				resp.OutputStream.Close();
			}
		}

		// ── POST helpers ──────────────────────────────────────────────────────

		private static async Task<JsonElement> ReadJsonBodyAsync(HttpListenerRequest req)
		{
			using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
			var raw = await reader.ReadToEndAsync();
			if (string.IsNullOrWhiteSpace(raw))
			{
				// Empty body → empty object, so all GetX fall back to defaults.
				using var empty = JsonDocument.Parse("{}");
				return empty.RootElement.Clone();
			}
			using var doc = JsonDocument.Parse(raw);
			return doc.RootElement.Clone();
		}

		private static async Task WriteOkAsync(HttpListenerResponse resp)
		{
			var data = Encoding.UTF8.GetBytes("{\"ok\":true}");
			resp.ContentType = "application/json";
			resp.ContentLength64 = data.Length;
			await resp.OutputStream.WriteAsync(data);
		}

		private static int GetInt(JsonElement e, string name, int fallback)
			=> e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;

		private static double GetDouble(JsonElement e, string name, double fallback)
			=> e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

		private static string GetString(JsonElement e, string name, string fallback)
			=> e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : fallback;

		private static int? GetNullableInt(JsonElement e, string name)
			=> e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : (int?)null;

		private static bool? GetNullableBool(JsonElement e, string name)
		{
			if (!e.TryGetProperty(name, out var v))
			{
				return null;
			}
			if (v.ValueKind == JsonValueKind.True)  return true;
			if (v.ValueKind == JsonValueKind.False) return false;
			return null;
		}
	}
}
#endif
