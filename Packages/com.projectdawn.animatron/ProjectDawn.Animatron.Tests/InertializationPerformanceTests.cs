using NUnit.Framework;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.PerformanceTesting;

namespace ProjectDawn.Animation.Tests
{
    [Category("Performance")]
    public class InertializationPerformanceTests
    {
        [Test, Performance]
        public void InertializationPerformanceTests_Create()
        {
            Measure.Method(() =>
                {
                    new InertializationPerformanceTests_Create_Job { Count = 10000,}.Run();
                })
                .WarmupCount(5)
                .IterationsPerMeasurement(1)
                .MeasurementCount(20)
                .Run();
        }

        [BurstCompile]
        struct InertializationPerformanceTests_Create_Job : IJob
        {
            public int Count;

            [MethodImpl(MethodImplOptions.NoOptimization)]
            public void Execute()
            {
                for (int i = 0; i < Count; i++)
                {
                    var random = new Random(1);
                    var x0 = random.NextFloat(-100, 100);
                    var v0 = random.NextFloat(-100, 100);
                    var t1 = random.NextFloat(0.1f, 0.5f);

                    _ = InertializationCoefficients.Create(x0, v0, t1);
                }
            }
        }

        [Test, Performance]
        public void InertializationPerformanceTests_Create_Optimized()
        {
            Measure.Method(() =>
            {
                new InertializationPerformanceTests_Create_Optimized_Job { Count = 10000, }.Run();
            })
                .WarmupCount(5)
                .IterationsPerMeasurement(1)
                .MeasurementCount(20)
                .Run();
        }

        [BurstCompile]
        struct InertializationPerformanceTests_Create_Optimized_Job : IJob
        {
            public int Count;

            [MethodImpl(MethodImplOptions.NoOptimization)]
            public void Execute()
            {
                for (int i = 0; i < Count; i++)
                {
                    var random = new Random(1);
                    var x0 = random.NextFloat(-100, 100);
                    var v0 = random.NextFloat(-100, 100);
                    var t1 = random.NextFloat(0.1f, 0.5f);

                    _ = InertializationCoefficientsOptimized.Create(x0, v0, t1);
                }
            }
        }

        [Test, Performance]
        public void InertializationPerformanceTests_Eveluate()
        {
            Measure.Method(() =>
            {
                new InertializationPerformanceTests_Eveluate_Job { Count = 10000, }.Run();
            })
                .WarmupCount(5)
                .IterationsPerMeasurement(1)
                .MeasurementCount(20)
                .Run();
        }

        [BurstCompile]
        struct InertializationPerformanceTests_Eveluate_Job : IJob
        {
            public int Count;

            [MethodImpl(MethodImplOptions.NoOptimization)]
            public void Execute()
            {
                var random = new Random(1);
                var x0 = random.NextFloat(-100, 100);
                var v0 = random.NextFloat(-100, 100);
                var t1 = random.NextFloat(0.1f, 0.5f);
                var inertialization = InertializationCoefficients.Create(x0, v0, t1);

                for (int i = 0; i < Count; i++)
                {
                    var timePower = new TimePower(random.NextFloat(0, 1));
                    _ = inertialization.Evaluate(timePower);
                }
            }
        }

        [Test, Performance]
        public void InertializationPerformanceTests_Eveluate_Optimized()
        {
            Measure.Method(() =>
            {
                new InertializationPerformanceTests_Eveluate_Optimized_Job { Count = 10000, }.Run();
            })
                .WarmupCount(5)
                .IterationsPerMeasurement(1)
                .MeasurementCount(20)
                .Run();
        }

        [BurstCompile]
        struct InertializationPerformanceTests_Eveluate_Optimized_Job : IJob
        {
            public int Count;

            [MethodImpl(MethodImplOptions.NoOptimization)]
            public void Execute()
            {
                var random = new Random(1);
                var x0 = random.NextFloat(-100, 100);
                var v0 = random.NextFloat(-100, 100);
                var t1 = random.NextFloat(0.1f, 0.5f);
                var inertialization = InertializationCoefficientsOptimized.Create(x0, v0, t1);

                for (int i = 0; i < Count; i++)
                {
                    var timePower = new TimePower(random.NextFloat(0, 1));
                    _ = inertialization.Evaluate(timePower);
                }
            }
        }
    }
}