using System;
using System.Threading.Tasks;

namespace Alluvial
{
    public static class StreamCatchup
    {
        public static IStreamCatchup<TData> Create<TData>(
            IStream<IStream<TData>> source,
            ICursor cursor = null,
            int? batchCount = null,
            Action<CatchupConfiguration> configure = null)
        {
            var configuration = new CatchupConfiguration();
            if (configure!=null)
            {
                configure(configuration);
            }

            return new StreamOfStreamsCatchup<TData>(
                source, 
                cursor, 
                batchCount,
                configuration.GetCursor,
                configuration.StoreCursor);
        }
        
        public static IStreamCatchup<TData> Create<TData>(
            IStream<TData> source,
            ICursor cursor = null,
            int? batchCount = null,
            Action<CatchupConfiguration> configure = null)
        {
            var configuration = new CatchupConfiguration();
            if (configure!=null)
            {
                configure(configuration);
            }

            return new SingleStreamCatchup<TData>(
                source, 
                batchCount);
        }

        /// <summary>
        /// Runs the catchup query until it reaches an empty batch, then stops.
        /// </summary>
        public static async Task<ICursor> RunUntilCaughtUp<TData>(this IStreamCatchup<TData> catchup)
        {
            ICursor cursor;
            var counter = new Progress<TData>();

            using (catchup.Subscribe<Progress<TData>, TData>(async (_, batch) => counter.Count(batch)))
            {
                int countBefore;
                do
                {
                    countBefore = counter.AggregatedCount;
                    cursor = await catchup.RunSingleBatch();
                } while (countBefore != counter.AggregatedCount);
            }

            return cursor;
        }

        public static IDisposable Poll<TData>(
            this IStreamCatchup<TData> catchup,
            TimeSpan pollInterval)
        {
            var canceled = false;

            Task.Run(async () =>
                           {
                               while (!canceled)
                               {
                                   await catchup.RunUntilCaughtUp();
                                   await Task.Delay(pollInterval);
                               }
                           });

            return Disposable.Create(() =>
            {
                canceled = true;
            });
        }

        public static IDisposable Subscribe<TProjection, TData>(
            this IStreamCatchup<TData> catchup,
            IStreamAggregator<TProjection, TData> aggregator,
            IProjectionStore<string, TProjection> projectionStore = null)
        {
            return catchup.SubscribeAggregator(aggregator, projectionStore);
        }

        public static IDisposable Subscribe<TProjection, TData>(
            this IStreamCatchup<TData> catchup,
             Action<TProjection, IStreamBatch<TData>> aggregate,
            IProjectionStore<string, TProjection> projectionStore = null)
        {
            return catchup.SubscribeAggregator(Aggregator.Create(aggregate), projectionStore);
        }

        public static IDisposable Subscribe<TProjection, TData>(
            this IStreamCatchup<TData> catchup,
            AggregateAsync<TProjection, TData> aggregate,
            IProjectionStore<string, TProjection> projectionStore = null)
        {
            return catchup.SubscribeAggregator(Aggregator.Create(aggregate), projectionStore);
        }

        public static IDisposable Subscribe<TProjection, TData>(
            this IStreamCatchup<TData> catchup,
            Func<TProjection, IStreamBatch<TData>, Task> aggregate,
            IProjectionStore<string, TProjection> projectionStore = null)
        {
            return catchup.SubscribeAggregator(Aggregator.Create(aggregate), projectionStore);
        }

        public static CatchupConfiguration StoreCursor(
            this CatchupConfiguration configuration,
            StoreCursor put)
        {
            configuration.StoreCursor = put;
            return configuration;
        }

        public static CatchupConfiguration GetCursor(
            this CatchupConfiguration configuration,
            GetCursor get)
        {
            configuration.GetCursor = get;
            return configuration;
        }

        internal class Progress<TData>
        {
            public Progress<TData> Count(IStreamBatch<TData> batch)
            {
                AggregatedCount += batch.Count;
                return this;
            }

            public int AggregatedCount { get; private set; }
        }
    }

}