using NUnit.Framework;
using Unity.Mathematics;

namespace ProjectDawn.Animation.Tests
{
    public class InertializationTests
    {
        [Test]
        public void InertializationTests_Regular_And_Optimized_Matches()
        {
            var random = new Random(1);
            for (int i = 0; i < 1000; i++)
            {
                var x0 = random.NextFloat(-100, 100);
                var v0 = random.NextFloat(-100, 100);
                var t1 = random.NextFloat(0.1f, 0.5f);

                var inertializer = InertializationCoefficients.Create(x0, v0, t1);
                var inertializerOptimized = InertializationCoefficients.Create(x0, v0, t1);

                for (int sampleIndex = 0; sampleIndex < 100; sampleIndex++)
                {
                    var time = random.NextFloat(0, 1);
                    var timePower = new TimePower(time);

                    var inertializerValue = inertializer.Evaluate(timePower);
                    var inertializerOptimizedValue = inertializer.Evaluate(timePower);

                    Assert.AreEqual(inertializerValue, inertializerOptimizedValue);
                }
            }
        }

        [Test]
        public void InertializationTests_Float3Inertia_Works()
        {
            var random = new Random(1);
            for (int i = 0; i < 1000; i++)
            {
                var previous = random.NextFloat3(-5, 5);
                var source = random.NextFloat3(-5, 5);
                var target = random.NextFloat3(-5, 5);
                var dt = random.NextFloat(1f / 30f, 1f / 60f);
                var duration = random.NextFloat(0.1f, 0.5f);

                var inertia = Float3Inertia.Create(previous, source, target, dt, duration);

                var timePower = new TimePower(0);
                var source2 = target + inertia.Evaluate(timePower);

                Assert.Zero(math.distance(source, source2));
            }
        }

        [Test]
        public void InertializationTests_QuaternionInertia_Works()
        {
            var random = new Random(1);
            for (int i = 0; i < 1000; i++)
            {
                var previous = random.NextQuaternionRotation();
                var source = random.NextQuaternionRotation();
                var target = random.NextQuaternionRotation();
                var dt = random.NextFloat(1f / 30f, 1f / 60f);
                var duration = random.NextFloat(0.1f, 0.5f);

                var inertia = QuaternionInertia.Create(previous, source, target, dt, duration);

                var timePower = new TimePower(0);
                var source2 = math.mul(inertia.Evaluate(timePower), target);

                Assert.Less(math.distance(math.rotate(source, math.right()), math.rotate(source2, math.right())), 0.001f);
            }
        }
    }
}