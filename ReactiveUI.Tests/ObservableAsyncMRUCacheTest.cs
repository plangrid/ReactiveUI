﻿using System;
using System.Concurrency;
using System.Linq;
using System.Threading;
using Xunit;

namespace ReactiveUI.Tests
{
    public class ObservableAsyncMRUCacheTest : IEnableLogger
    {
        [Fact]
        public void GetTest()
        {
            var input = new[] {1, 1, 1, 1, 1};
            var sched = new TestScheduler();
            ObservableAsyncMRUCache<int, int> fixture;

            var delay = TimeSpan.FromSeconds(1.0);
            fixture = new ObservableAsyncMRUCache<int, int>(x => Observable.Return(x*5).Delay(delay, sched), 5, 2);

            int result = 0;
            var t = new Thread(() => {
                foreach (int x in input.Select(x => fixture.Get(x))) {
                    result += x;
                }
            });
            t.Start();

            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(500)));

            Thread.Sleep(100);
            Assert.Equal(0, result);

            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(1200)));

            Thread.Sleep(100);
            Assert.Equal(25, result);

            this.Log().Info("Running to end");
            sched.Run();
            t.Join();
            Assert.Equal(25, result);
        }

        [Fact]
        public void AsyncGetTest()
        {
            var input = new[] { 1, 1, 1, 1, 1 };
            var sched = new TestScheduler();

            var delay = TimeSpan.FromSeconds(1.0);
            var fixture = new ObservableAsyncMRUCache<int, int>(x => Observable.Return(x*5).Delay(delay, sched), 5, 2, null, sched);

            int result = 0;
            input.ToObservable(sched).SelectMany<int, int>(x => (IObservable<int>)fixture.AsyncGet(x)).Subscribe(x => result += x);

            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(500)));
            Assert.Equal(0, result);

            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(1200)));
            Assert.Equal(25, result);

            this.Log().Info("Running to end");
            sched.Run();
            Assert.Equal(25, result);
        }

#if FALSE
        [Fact]
        public void CachedValuesTest()
        {
            var input = new[] { 1, 2, 1, 3, 1 };
            var sched = new TestScheduler();

            var delay = TimeSpan.FromSeconds(1.0);
            var fixture = new ObservableAsyncMRUCache<int, int>(x => Observable.Return(x*5).Delay(delay, sched), 2, 2);

            var results = input.ToObservable().SelectMany(fixture.AsyncGet).CreateCollection();
            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(500)));

            Assert.Equal(0, fixture.CachedValues().Count());

            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(1500)));

            var output = fixture.CachedValues().ToArray();
            Assert.IsTrue(output.Length == 2);
            Assert.Equal(input.Length, results.Count);
        }
#endif

        [Fact]
        public void CacheShouldQueueOnceWeHitOurConcurrentLimit()
        {
            var input = new[] { 1, 2, 3, 4, 1 };
            var sched = new TestScheduler();

            var delay = TimeSpan.FromSeconds(1.0);
            var fixture = new ObservableAsyncMRUCache<int, int>(x => Observable.Return(x*5).Delay(delay, sched), 5, 2, null, sched);

            int result = 0;
            input.ToObservable(sched).SelectMany<int, int>(x => (IObservable<int>)fixture.AsyncGet(x)).Subscribe(x => result += x);

            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(500)));
            Assert.Equal(0, result);

            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(1500)));
            Assert.Equal(1*5 + 2*5 + 1*5, result);

            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(2500)));
            Assert.Equal(1*5 + 2*5 + 3*5 + 4*5 + 1*5, result);

            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(5000)));
            Assert.Equal(1*5 + 2*5 + 3*5 + 4*5 + 1*5, result);
        }

        [Fact]
        public void CacheShouldEatExceptionsAndMarshalThemToObservable()
        {
            /* This is a bit tricky:
             * 
             * 5,2 complete at t=1000 simultaneously
             * 10,0 get queued up, 0 fails immediately (delay() doesn't delay the OnError), 
             *    so 10 completes at t=2000
             * The 7 completes at t=3000
             */
            var input = new[] { 5, 2, 10, 0/*boom!*/, 7 };
            var sched = new TestScheduler();

            Observable.Throw<int>(new Exception("Foo")).Subscribe(x => {
                Console.WriteLine(x);
            }, ex => {
                Console.WriteLine(ex);
            }, () => {
                Console.WriteLine("Completed");
            });

            var delay = TimeSpan.FromSeconds(1.0);
            var fixture = new ObservableAsyncMRUCache<int, int>(x => 
                (x == 0 ? Observable.Throw<int>(new Exception("Boom!")) : Observable.Return(10 * x)).Delay(delay, sched), 5, 2, null, sched);

            Exception exception = null;
            int completed = 0;
            input.ToObservable()
                .SelectMany(x => (IObservable<int>)fixture.AsyncGet(x))
                .Subscribe(x => {
                    this.Log().InfoFormat("Result = {0}", x);
                    completed++;
                }, ex => exception = exception ?? ex);

            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(500)));
            Assert.Null(exception);
            Assert.Equal(0, completed);

            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(1500)));
            Assert.NotNull(exception);
            Assert.Equal(2, completed);

            sched.RunTo(sched.FromTimeSpan(TimeSpan.FromMilliseconds(7500)));
            Assert.NotNull(exception);
            Assert.Equal(4, completed);
            this.Log().Info(exception);
        }
    }
}