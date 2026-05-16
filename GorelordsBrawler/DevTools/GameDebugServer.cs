#if DEBUG
using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GorelordsBrawler.DevTools
{
	/// <summary>
	/// Minimal HTTP server (port 7777) for automated game testing.
	///
	/// GET  /screenshot — returns the current rendered frame as PNG (blocks until next Draw)
	/// GET  /state      — returns a JSON snapshot of acid + player state
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

					resp.ContentType = "image/png";
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
	}
}
#endif
