using System.Linq;
using GorelordsBrawler.Components.Hazards.Fluid;
using Xunit;
using Xunit.Abstractions;

namespace Fluid.Tests
{
	public class DiagnosticTests
	{
		private readonly ITestOutputHelper _output;
		public DiagnosticTests(ITestOutputHelper output) => _output = output;

		[Fact]
		public void Diagnose_Stack_Settle()
		{
			var sim       = new FluidSimulation(600, 0, 0, 200, 400);
			var colliders = new FluidCollider(0, 200, 0, 400);

			float r = FluidConfig.ParticleRadius;
			for (int row = 0; row < 25; row++)
			{
				for (int col = 0; col < 20; col++)
				{
					sim.Spawn(10 + col * (2 * r + 1), 50 + row * (2 * r + 1), 0, 0);
				}
			}

			_output.WriteLine($"RestDensity = {sim.RestDensity:F6}");

			int[] checkpoints = { 30, 60, 120, 240, 360, 480, 600 };
			int next = 0;
			for (int frame = 0; frame < 600; frame++)
			{
				sim.Step(1f / 60f, colliders);
				if (next < checkpoints.Length && frame + 1 == checkpoints[next])
				{
					float minY = float.MaxValue, maxY = float.MinValue;
					float maxVy = 0f;
					for (int i = 0; i < sim.Count; i++)
					{
						if (sim.Py[i] < minY) minY = sim.Py[i];
						if (sim.Py[i] > maxY) maxY = sim.Py[i];
						float av = System.MathF.Abs(sim.Vy[i]);
						if (av > maxVy) maxVy = av;
					}
					_output.WriteLine($"frame={frame + 1,4}  count={sim.Count}  minY={minY,7:F2}  maxY={maxY,7:F2}  span={maxY - minY,6:F2}  maxAbsVy={maxVy,7:F2}");
					next++;
				}
			}
		}
	}
}
