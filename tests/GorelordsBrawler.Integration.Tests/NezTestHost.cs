namespace GorelordsBrawler.Integration.Tests;

/// <summary>
/// Placeholder for tests that require a running MonoGame/Nez context (Entity, Scene, etc.).
///
/// To enable:
///   1. Set SDL_VIDEODRIVER=offscreen (Linux) or run on a machine with a display (Windows).
///   2. Create a minimal Nez.Core subclass that calls base.Initialize() without showing a window.
///   3. Run a fixed number of update frames via reflection or a controlled game loop.
///
/// Tests in this class are skipped by default.  When the host is available, remove the
/// [Fact(Skip = "...")] attribute and invoke NezTestHost.Initialize() in your fixture.
///
/// See: https://github.com/MonoGame/MonoGame/issues/7444 for headless MonoGame discussion.
/// </summary>
public static class NezTestHost
{
	/// <summary>
	/// True if the MonoGame/Nez runtime has been successfully initialized headlessly.
	/// Check this guard before running any test that calls Entity.AddComponent or Scene.CreateEntity.
	/// </summary>
	public static bool IsAvailable => false;

	// When implementing: call Core.Initialize() equivalent and set IsAvailable = true.
	// Use Core.Scene = new Scene() to give tests a minimal scene context.
}
