﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Egodystonic.Atomics.Numerics;
using Egodystonic.Atomics.Tests.DummyObjects;
using Egodystonic.Atomics.Tests.Harness;
using Egodystonic.Atomics.Tests.UnitTests.Common;
using NUnit.Framework;
using static Egodystonic.Atomics.Tests.Harness.ConcurrentTestCaseRunner;
using ImmutableVal = Egodystonic.Atomics.Tests.DummyObjects.DummyImmutableVal;
using EquatableVal = Egodystonic.Atomics.Tests.DummyObjects.DummyImmutableValAlphaOnlyEquatable;
using SixteenVal = Egodystonic.Atomics.Tests.DummyObjects.DummySixteenByteVal;

namespace Egodystonic.Atomics.Tests.UnitTests {
	[TestFixture]
	class AtomicValTest : CommonAtomicValTestSuite<AtomicVal<ImmutableVal>> {
		#region Test Fields
		RunnerFactory<EquatableVal, AtomicVal<EquatableVal>> _alphaOnlyEquatableRunnerFactory;
		RunnerFactory<SixteenVal, AtomicVal<SixteenVal>> _sixteenByteRunnerFactory;

		protected override ImmutableVal Alpha { get; } = new ImmutableVal(1, 1);
		protected override ImmutableVal Bravo { get; } = new ImmutableVal(2, 2);
		protected override ImmutableVal Charlie { get; } = new ImmutableVal(3, 3);
		protected override ImmutableVal Delta { get; } = new ImmutableVal(4, 4);
		protected override bool AreEqual(ImmutableVal lhs, ImmutableVal rhs) => lhs == rhs;
		#endregion

		#region Test Setup
		[OneTimeSetUp]
		public void SetUpClass() {
			_alphaOnlyEquatableRunnerFactory = new RunnerFactory<EquatableVal, AtomicVal<EquatableVal>>();
			_sixteenByteRunnerFactory = new RunnerFactory<SixteenVal, AtomicVal<SixteenVal>>();
		}

		[OneTimeTearDown]
		public void TearDownClass() { }

		[SetUp]
		public void SetUpTest() { }

		[TearDown]
		public void TearDownTest() { }
		#endregion

		#region Custom Equality Tests
		[Test]
		public void API_SpinWaitForValue_CustomEquality() {
			var target = new AtomicVal<EquatableVal>(new EquatableVal(0, 0));
			var task = Task.Run(() => target.SpinWaitForValue(new EquatableVal(1, 1)));
			target.Set(new EquatableVal(1, 100));
			Assert.AreEqual(new EquatableVal(1, 100), task.Result);
		}

		[Test]
		public void API_SpinWaitForExchange_CustomEquality() {
			var target = new AtomicVal<EquatableVal>(new EquatableVal(0, 0));
			var task = Task.Run(() => target.SpinWaitForExchange(new EquatableVal(1, 1), new EquatableVal(10, 10)));
			target.Set(new EquatableVal(10, 100));
			Assert.AreEqual((new EquatableVal(10, 100), new EquatableVal(1, 1)), task.Result);

			task = Task.Run(() => target.SpinWaitForExchange((c, ctx) => new EquatableVal(c.Alpha + ctx, c.Bravo + ctx), new EquatableVal(100, 100), 1));
			target.Set(new EquatableVal(100, 1000));
			Assert.AreEqual((new EquatableVal(100, 1000), new EquatableVal(101, 1001)), task.Result);
		}

		[Test]
		public void API_TryExchange_CustomEquality() {
			var target = new AtomicVal<EquatableVal>(new EquatableVal(0, 0));
			Assert.AreEqual((false, new EquatableVal(0, 0), new EquatableVal(0, 0)), target.TryExchange(new EquatableVal(10, 10), new EquatableVal(1, 1)));
			target.Set(new EquatableVal(1, 0));
			Assert.AreEqual((true, new EquatableVal(1, 0), new EquatableVal(10, 10)), target.TryExchange(new EquatableVal(10, 10), new EquatableVal(1, 1)));

			target.Set(new EquatableVal(0, 0));
			Assert.AreEqual((false, new EquatableVal(0, 0), new EquatableVal(0, 0)), target.TryExchange((c, ctx) => new EquatableVal(10, c.Bravo + ctx), new EquatableVal(1, 1), 10));
			target.Set(new EquatableVal(1, 0));
			Assert.AreEqual((true, new EquatableVal(1, 0), new EquatableVal(10, 10)), target.TryExchange((c, ctx) => new EquatableVal(10, c.Bravo + ctx), new EquatableVal(1, 1), 10));
		}
		#endregion

		#region Oversized Value Type Test
		[Test]
		public void GetAndSetAndValue_Oversized() {
			const int NumIterations = 1_000_000;
			var atomicLong = new AtomicLong(0L);
			var runner = _sixteenByteRunnerFactory.NewRunner(new SixteenVal(0, 0));

			runner.ExecuteContinuousSingleWriterCoherencyTests(
				target => {
					unsafe {
						var newLongVal = atomicLong.Increment().NewValue;
						target.Set(*(SixteenVal*) &newLongVal);
					}
				},
				NumIterations,
				target => target.Get(),
				(prev, cur) => {
					unsafe {
						AssertTrue(*(long*) &prev <= *(long*) &cur);
					}
				}
			);

			runner.ExecuteContinuousSingleWriterCoherencyTests(
				target => {
					unsafe {
						var newLongVal = atomicLong.Increment().NewValue;
						target.Value = *(SixteenVal*) &newLongVal;
					}
				},
				NumIterations,
				target => target.Value,
				(prev, cur) => {
					unsafe {
						AssertTrue(*(long*) &prev <= *(long*) &cur);
					}
				}
			);
		}

		[Test]
		public void SpinWaitForValue_Oversized() {
			const int NumIterations = 300_000;

			var runner = _sixteenByteRunnerFactory.NewRunner(new SixteenVal(0, 0));

			// (T)
			runner.AllThreadsTearDown = target => AssertAreEqual(NumIterations, target.Value.Alpha);
			runner.ExecuteSingleWriterSingleReaderTests(
				target => {
					while (true) {
						var curVal = target.Value;
						if (curVal.Alpha == NumIterations) break;
						if ((curVal.Alpha & 1) == 0) {
							AssertAreEqual(curVal.Alpha + 1, target.SpinWaitForValue(new SixteenVal(curVal.Alpha + 1, 0)).Alpha);
						}
						else {
							target.Value = new SixteenVal(curVal.Alpha + 1, 0);
						}
					}
				},
				target => {
					while (true) {
						var curVal = target.Value;
						if (curVal.Alpha == NumIterations) break;
						if ((curVal.Alpha & 1) == 1) {
							AssertAreEqual(curVal.Alpha + 1, target.SpinWaitForValue(new SixteenVal(curVal.Alpha + 1, 0)).Alpha);
						}
						else {
							target.Value = new SixteenVal(curVal.Alpha + 1, 0);
						}
					}
				}
			);

			// (Func<T, bool>)
			runner.AllThreadsTearDown = target => AssertAreEqual(NumIterations, target.Value.Alpha);
			runner.ExecuteWriterReaderTests(
				target => {
					while (true) {
						var curVal = target.Value;
						if (curVal.Alpha == NumIterations) break;
						target.TryExchange(new SixteenVal(curVal.Alpha + 1, 0), curVal);
					}
				},
				target => {
					while (true) {
						var curVal = target.Value;
						if (curVal.Alpha == NumIterations) break;
						AssertTrue(target.SpinWaitForValue(c => c.Alpha > curVal.Alpha).Alpha >= curVal.Alpha);
					}
				}
			);

			// (Func<T, TContext, bool>, TContext)
			runner.AllThreadsTearDown = target => AssertAreEqual(NumIterations, target.Value.Alpha);
			runner.ExecuteWriterReaderTests(
				target => {
					while (true) {
						var curVal = target.Value;
						if (curVal.Alpha == NumIterations) break;
						target.TryExchange(new SixteenVal(curVal.Alpha + 1, 0), curVal);
					}
				},
				target => {
					while (true) {
						var curVal = target.Value;
						if (curVal.Alpha == NumIterations) break;
						AssertTrue(target.SpinWaitForValue((c, ctx) => c.Alpha > ctx.Alpha, curVal).Alpha >= curVal.Alpha);
					}
				}
			);
		}

		[Test]
		public void Exchange_Oversized() {
			const int NumIterations = 300_000;

			var atomicIntA = new AtomicInt(0);
			var atomicIntB = new AtomicInt(0);
			var runner = _sixteenByteRunnerFactory.NewRunner(new SixteenVal(0, 0));

			// (T)
			runner.GlobalSetUp = (_, __) => { atomicIntA.Set(0); atomicIntB.Set(0); };
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(NumIterations, target.Value.Bravo);
			};
			runner.ExecuteContinuousSingleWriterCoherencyTests(
				target => {
					var newA = atomicIntA.Increment().NewValue;
					var newB = atomicIntB.Increment().NewValue;
					var newValue = new SixteenVal(newA, newB);
					var prev = target.Exchange(newValue).PreviousValue;
					AssertAreEqual(prev.Alpha, newA - 1);
					AssertAreEqual(prev.Bravo, newB - 1);
				},
				NumIterations,
				target => target.Value,
				(prev, cur) => {
					AssertTrue(prev.Alpha <= cur.Alpha);
					AssertTrue(prev.Bravo <= cur.Bravo);
				}
			);
			runner.GlobalSetUp = null;
			runner.AllThreadsTearDown = null;

			// (Func<T, T>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(NumIterations, target.Value.Bravo);
			};
			runner.ExecuteFreeThreadedTests(
				target => {
					var exchRes = target.Exchange(t => new SixteenVal(t.Alpha + 1, t.Bravo + 1));
					AssertAreEqual(exchRes.PreviousValue.Alpha + 1, exchRes.NewValue.Alpha);
					AssertAreEqual(exchRes.PreviousValue.Bravo + 1, exchRes.NewValue.Bravo);
				},
				NumIterations
			);
			runner.AllThreadsTearDown = null;

			// (Func<T, TContext, T>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(NumIterations * 2, target.Value.Bravo);
			};
			runner.ExecuteFreeThreadedTests(
				target => {
					var exchRes = target.Exchange((t, ctx) => new SixteenVal(t.Alpha + 1, t.Bravo + ctx), 2);
					AssertAreEqual(exchRes.PreviousValue.Alpha + 1, exchRes.NewValue.Alpha);
					AssertAreEqual(exchRes.PreviousValue.Bravo + 2, exchRes.NewValue.Bravo);
				},
				NumIterations
			);
			runner.AllThreadsTearDown = null;
		}

		[Test]
		public void SpinWaitForExchangeWithoutContext_Oversized() {
			const int NumIterations = 100_000;

			var runner = _sixteenByteRunnerFactory.NewRunner(new SixteenVal(0, 0));

			// (T, T)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(NumIterations, target.Value.Bravo);
			};
			runner.ExecuteSingleWriterSingleReaderTests(
				target => {
					for (var i = 0; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange(new SixteenVal(nextVal + 1, nextVal + 1), new SixteenVal(nextVal, nextVal));
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				},
				target => {
					for (var i = 1; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange(new SixteenVal(nextVal + 1, nextVal + 1), new SixteenVal(nextVal, nextVal));
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (Func<T, T>, T)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(-NumIterations, target.Value.Bravo);
			};
			runner.ExecuteSingleWriterSingleReaderTests(
				target => {
					for (var i = 0; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange(c => new SixteenVal(c.Alpha + 1, c.Bravo - 1), new SixteenVal(nextVal, -nextVal));
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(-nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(-(nextVal + 1), exchRes.NewValue.Bravo);
					}
				},
				target => {
					for (var i = 1; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange(c => new SixteenVal(c.Alpha + 1, c.Bravo - 1), new SixteenVal(nextVal, -nextVal));
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(-nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(-(nextVal + 1), exchRes.NewValue.Bravo);
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (T, Func<T, T, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(NumIterations, target.Value.Bravo);
			};
			runner.ExecuteSingleWriterSingleReaderTests(
				target => {
					for (var i = 0; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange(new SixteenVal(nextVal + 1, nextVal + 1), (c, n) => n.Alpha == c.Alpha + 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				},
				target => {
					for (var i = 1; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange(new SixteenVal(nextVal + 1, nextVal + 1), (c, n) => n.Alpha == c.Alpha + 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (Func<T, T>, Func<T, T, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(NumIterations, target.Value.Bravo);
			};
			runner.ExecuteSingleWriterSingleReaderTests(
				target => {
					for (var i = 0; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange(c => new SixteenVal(nextVal + 1, c.Bravo + 1), (c, n) => n.Alpha == c.Alpha + 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				},
				target => {
					for (var i = 1; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange(c => new SixteenVal(nextVal + 1, c.Bravo + 1), (c, n) => n.Alpha == c.Alpha + 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				}
			);
			runner.AllThreadsTearDown = null;
		}

		[Test]
		public void SpinWaitForExchangeWithContext_Oversized() {
			const int NumIterations = 100_000;

			var runner = _sixteenByteRunnerFactory.NewRunner(new SixteenVal(0, 0));

			// (Func<T, TContext, T>, T)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(NumIterations, target.Value.Bravo);
			};
			runner.ExecuteSingleWriterSingleReaderTests(
				target => {
					for (var i = 0; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange((c, ctx) => new SixteenVal(ctx, c.Bravo + 1), new SixteenVal(nextVal, nextVal), nextVal + 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				},
				target => {
					for (var i = 1; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange((c, ctx) => new SixteenVal(ctx, c.Bravo + 1), new SixteenVal(nextVal, nextVal), nextVal + 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (T, Func<T, T, TContext, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(NumIterations, target.Value.Bravo);
			};
			runner.ExecuteSingleWriterSingleReaderTests(
				target => {
					for (var i = 0; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange(new SixteenVal(nextVal + 1, nextVal + 1), (c, n, ctx) => n.Alpha == c.Alpha + ctx, 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				},
				target => {
					for (var i = 1; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange(new SixteenVal(nextVal + 1, nextVal + 1), (c, n, ctx) => n.Alpha == c.Alpha + ctx, 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (Func<T, TContext, T>, Func<T, T, TContext, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(NumIterations, target.Value.Bravo);
			};
			runner.ExecuteSingleWriterSingleReaderTests(
				target => {
					for (var i = 0; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange((c, ctx) => new SixteenVal(ctx, c.Bravo + 1), (c, n, ctx) => n.Alpha == c.Alpha + 1 && n.Bravo == ctx, nextVal + 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				},
				target => {
					for (var i = 1; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange((c, ctx) => new SixteenVal(ctx, c.Bravo + 1), (c, n, ctx) => n.Alpha == c.Alpha + 1 && n.Bravo == ctx, nextVal + 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (Func<T, TMapContext, T>, Func<T, T, TPredicateContext, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(NumIterations, target.Value.Bravo);
			};
			runner.ExecuteSingleWriterSingleReaderTests(
				target => {
					for (var i = 0; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange((c, ctx) => new SixteenVal(ctx, c.Bravo + 1), (c, n, ctx) => n.Alpha == c.Alpha + ctx, nextVal + 1, 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				},
				target => {
					for (var i = 1; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange((c, ctx) => new SixteenVal(ctx, c.Bravo + 1), (c, n, ctx) => n.Alpha == c.Alpha + ctx, nextVal + 1, 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (Func<T, TContext, T>, Func<T, T, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(NumIterations, target.Value.Bravo);
			};
			runner.ExecuteSingleWriterSingleReaderTests(
				target => {
					for (var i = 0; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange((c, ctx) => new SixteenVal(ctx, c.Bravo + 1), (c, n) => n.Alpha == c.Alpha + 1, nextVal + 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				},
				target => {
					for (var i = 1; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange((c, ctx) => new SixteenVal(ctx, c.Bravo + 1), (c, n) => n.Alpha == c.Alpha + 1, nextVal + 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (Func<T, T>, Func<T, T, TContext, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(NumIterations, target.Value.Bravo);
			};
			runner.ExecuteSingleWriterSingleReaderTests(
				target => {
					for (var i = 0; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange(c => new SixteenVal(nextVal + 1, c.Bravo + 1), (c, n, ctx) => n.Alpha == c.Alpha + ctx, 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				},
				target => {
					for (var i = 1; i < NumIterations; i += 2) {
						var nextVal = i;
						var exchRes = target.SpinWaitForExchange(c => new SixteenVal(nextVal + 1, c.Bravo + 1), (c, n, ctx) => n.Alpha == c.Alpha + ctx, 1);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Alpha);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Alpha);
						AssertAreEqual(nextVal, exchRes.PreviousValue.Bravo);
						AssertAreEqual(nextVal + 1, exchRes.NewValue.Bravo);
					}
				}
			);
			runner.AllThreadsTearDown = null;
		}

		[Test]
		public void TryExchangeWithoutContext_Oversized() {
			const int NumIterations = 200_000;

			var runner = _sixteenByteRunnerFactory.NewRunner(new SixteenVal(0, 0));

			// (T, T)
			runner.ExecuteContinuousCoherencyTests(
				target => {
					var curValue = target.Value;
					var newValue = new SixteenVal(0, curValue.Bravo + 1);
					target.TryExchange(newValue, curValue);
				},
				NumIterations,
				target => target.Value,
				(prev, cur) => AssertTrue(cur.Bravo >= prev.Bravo)
			);

			// (T, Func<T, T, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(-1 * NumIterations, target.Value.Bravo);
			};
			runner.ExecuteFreeThreadedTests(
				target => {
					while (true) {
						var curValue = target.Value;
						if (curValue.Alpha == NumIterations) return;
						var newValue = new SixteenVal(curValue.Alpha + 1, curValue.Bravo - 1);
						var (wasSet, prevValue, setValue) = target.TryExchange(newValue, (c, n) => c.Alpha + 1 == n.Alpha && c.Bravo - 1 == n.Bravo);
						if (wasSet) {
							AssertAreEqual(curValue, prevValue);
							AssertAreEqual(newValue, setValue);
						}
						else {
							AssertAreNotEqual(curValue, prevValue);
							AssertAreEqual(setValue, prevValue);
						}
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (Func<T, T>, T)
			runner.ExecuteFreeThreadedTests(
				target => {
					var curValue = target.Value;

					var (wasSet, prevValue, newValue) = target.TryExchange(
						c => c.Bravo < c.Alpha
							? new SixteenVal(c.Alpha, c.Bravo + 1)
							: new SixteenVal(c.Alpha + 1, c.Bravo),
						curValue
					);

					if (wasSet) {
						AssertAreEqual(curValue, prevValue);
						AssertAreEqual(prevValue.Bravo < prevValue.Alpha ? new SixteenVal(prevValue.Alpha, prevValue.Bravo + 1) : new SixteenVal(prevValue.Alpha + 1, prevValue.Bravo), newValue);
					}

					else AssertAreNotEqual(curValue, prevValue);
				},
				NumIterations
			);

			// (Func<T, T>, Func<T, T, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(-1 * NumIterations, target.Value.Bravo);
			};
			runner.ExecuteFreeThreadedTests(
				target => {
					while (true) {
						var curValue = target.Value;
						if (curValue.Alpha == NumIterations) return;
						var (wasSet, prevValue, newValue) = target.TryExchange(c => new SixteenVal(c.Alpha + 1, c.Bravo - 1), (c, n) => c.Alpha + 1 == n.Alpha && c.Bravo - 1 == n.Bravo && c.Alpha < NumIterations);
						if (wasSet) {
							AssertAreEqual(new SixteenVal(prevValue.Alpha + 1, prevValue.Bravo - 1), newValue);
							AssertTrue(newValue.Alpha <= NumIterations);
						}
						else AssertAreEqual(prevValue, newValue);
					}
				}
			);
			runner.AllThreadsTearDown = null;
		}

		[Test]
		public void TryExchangeWithContext_Oversized() {
			const int NumIterations = 300_000;

			var runner = _sixteenByteRunnerFactory.NewRunner(new SixteenVal(0, 0));

			// (T, Func<T, T, TContext, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(-1 * NumIterations, target.Value.Bravo);
			};
			runner.ExecuteFreeThreadedTests(
				target => {
					while (true) {
						var curValue = target.Value;
						if (curValue.Alpha == NumIterations) return;
						var newValue = new SixteenVal(curValue.Alpha + 1, curValue.Bravo - 1);
						var (wasSet, prevValue, setValue) = target.TryExchange(newValue, (c, n, ctx) => c.Alpha + ctx == n.Alpha && c.Bravo - ctx == n.Bravo, 1);
						if (wasSet) {
							AssertAreEqual(curValue, prevValue);
							AssertAreEqual(newValue, setValue);
						}
						else {
							AssertAreNotEqual(curValue, prevValue);
							AssertAreEqual(setValue, prevValue);
						}
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (Func<T, TContext, T>, T)
			runner.ExecuteFreeThreadedTests(
				target => {
					var curValue = target.Value;

					var (wasSet, prevValue, newValue) = target.TryExchange(
						(c, ctx) => c.Bravo < c.Alpha
							? new SixteenVal(c.Alpha, c.Bravo + ctx)
							: new SixteenVal(c.Alpha + ctx, c.Bravo),
						curValue,
						1
					);

					if (wasSet) {
						AssertAreEqual(curValue, prevValue);
						AssertAreEqual(prevValue.Bravo < prevValue.Alpha ? new SixteenVal(prevValue.Alpha, prevValue.Bravo + 1) : new SixteenVal(prevValue.Alpha + 1, prevValue.Bravo), newValue);
					}
					else AssertAreNotEqual(curValue, prevValue);
				},
				NumIterations
			);

			// (Func<T, TContext, T>, Func<T, T, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(-1 * NumIterations, target.Value.Bravo);
			};
			runner.ExecuteFreeThreadedTests(
				target => {
					while (true) {
						var curValue = target.Value;
						if (curValue.Alpha == NumIterations) return;
						var (wasSet, prevValue, newValue) = target.TryExchange((c, ctx) => new SixteenVal(c.Alpha + ctx, c.Bravo - ctx), (c, n) => c.Alpha + 1 == n.Alpha && c.Bravo - 1 == n.Bravo && c.Alpha < NumIterations, 1);
						if (wasSet) {
							AssertAreEqual(new SixteenVal(prevValue.Alpha + 1, prevValue.Bravo - 1), newValue);
							AssertTrue(newValue.Alpha <= NumIterations);
						}
						else AssertAreEqual(newValue, prevValue);
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (Func<T, T>, Func<T, T, TContext, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(-1 * NumIterations, target.Value.Bravo);
			};
			runner.ExecuteFreeThreadedTests(
				target => {
					while (true) {
						var curValue = target.Value;
						if (curValue.Alpha == NumIterations) return;
						var (wasSet, prevValue, newValue) = target.TryExchange(c => new SixteenVal(c.Alpha + 1, c.Bravo - 1), (c, n, ctx) => c.Alpha + ctx == n.Alpha && c.Bravo - ctx == n.Bravo && c.Alpha < NumIterations, 1);
						if (wasSet) {
							AssertAreEqual(new SixteenVal(prevValue.Alpha + 1, prevValue.Bravo - 1), newValue);
							AssertTrue(newValue.Alpha <= NumIterations);
						}
						else AssertAreEqual(newValue, prevValue);
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (Func<T, TContext, T>, Func<T, T, TContext, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(-1 * NumIterations, target.Value.Bravo);
			};
			runner.ExecuteFreeThreadedTests(
				target => {
					while (true) {
						var curValue = target.Value;
						if (curValue.Alpha == NumIterations) return;
						var (wasSet, prevValue, newValue) = target.TryExchange((c, ctx) => new SixteenVal(c.Alpha + ctx, c.Bravo - ctx), (c, n, ctx) => c.Alpha + ctx == n.Alpha && c.Bravo - ctx == n.Bravo && c.Alpha < NumIterations, 1);
						if (wasSet) {
							AssertAreEqual(new SixteenVal(prevValue.Alpha + 1, prevValue.Bravo - 1), newValue);
							AssertTrue(newValue.Alpha <= NumIterations);
						}
						else AssertAreEqual(newValue, prevValue);
					}
				}
			);
			runner.AllThreadsTearDown = null;

			// (Func<T, TMapContext, T>, Func<T, T, TPredicateContext, bool>)
			runner.AllThreadsTearDown = target => {
				AssertAreEqual(NumIterations, target.Value.Alpha);
				AssertAreEqual(-1 * NumIterations, target.Value.Bravo);
			};
			runner.ExecuteFreeThreadedTests(
				target => {
					while (true) {
						var curValue = target.Value;
						if (curValue.Alpha == NumIterations) return;
						var (wasSet, prevValue, newValue) = target.TryExchange((c, ctx) => new SixteenVal(c.Alpha + ctx, c.Bravo - ctx), (c, n, ctx) => c.Alpha + 1 == n.Alpha && c.Bravo - 1 == n.Bravo && c.Alpha < ctx, 1, NumIterations);
						if (wasSet) {
							AssertAreEqual(new SixteenVal(prevValue.Alpha + 1, prevValue.Bravo - 1), newValue);
							AssertTrue(newValue.Alpha <= NumIterations);
						}
						else AssertAreEqual(newValue, prevValue);
					}
				}
			);
			runner.AllThreadsTearDown = null;
		}
		#endregion

		#region Borrow Methods
		[Test]
		public void API_CreateScopedReadonlyRef() {
			var target = new AtomicVal<SixteenVal>(new SixteenVal(5, 20));

			var token = target.CreateScopedReadonlyRef();
			Assert.AreEqual(new SixteenVal(5, 20), token.Value);

			// Assert a read lock is taken until we dispose the token

			var getTask = Task.Run(() => target.Get());
			var setTask = Task.Run(() => target.Set(new SixteenVal(100, 100)));
			Thread.Sleep(400); // Give the test 'time to fail'

			Assert.AreEqual(false, setTask.IsCompleted);
			Assert.AreEqual(new SixteenVal(5, 20), token.Value);

			Assert.AreEqual(new SixteenVal(5, 20), getTask.Result);

			token.Dispose();

			setTask.Wait();
			Assert.AreEqual(new SixteenVal(100, 100), target.Value);
		}

		[Test]
		public void API_CreateScopedMutableRef() {
			var target = new AtomicVal<SixteenVal>(new SixteenVal(5, 20));

			var token = target.CreateScopedMutableRef();
			Assert.AreEqual(new SixteenVal(5, 20), token.Value);

			// Assert a write lock is taken until we dispose the token

			var getTask = Task.Run(() => target.Get());
			var setTask = Task.Run(() => target.CreateScopedMutableRef());
			Thread.Sleep(400); // Give the test 'time to fail'

			Assert.AreEqual(false, getTask.IsCompleted);
			Assert.AreEqual(false, setTask.IsCompleted);
			Assert.AreEqual(new SixteenVal(5, 20), token.Value);

			token.Value = new SixteenVal(20, 20);

			token.Dispose();
			setTask.Result.Dispose();

			Assert.AreEqual(new SixteenVal(20, 20), target.Value);
			Assert.AreEqual(new SixteenVal(20, 20), getTask.Result);
		}

		[Test]
		public void ScopedReadonlyRefShouldAllowMultipleConcurrentReaders() {
			const int NumIterations = 200_000;

			var runner = _sixteenByteRunnerFactory.NewRunner(new SixteenVal(1, 2));

			var remainingReadThreads = new AtomicInt(0);
			AtomicVal<SixteenVal>.ScopedReadonlyRefToken persistedReadToken = default;
			runner.GlobalSetUp = (target, threadConfig) => {
				persistedReadToken = target.CreateScopedReadonlyRef();
				remainingReadThreads.Set(threadConfig.ReaderThreadCount);
			};
			runner.AllThreadsTearDown = target => Assert.AreEqual(new SixteenVal(3, 4), target.Value);
			runner.ExecuteSingleWriterTests(
				writerFunction: target => target.Set(new SixteenVal(3, 4)),
				readerFunction: target => {
					for (var i = 0; i < NumIterations; i++) {
						AssertAreEqual(new SixteenVal(1, 2), target.Value);
						using (var tok = target.CreateScopedReadonlyRef()) {
							AssertAreEqual(new SixteenVal(1, 2), tok.Value);
						}
					}
					
					if (remainingReadThreads.Decrement().NewValue == 0) persistedReadToken.Dispose();
				}
			);
		}

		[Test]
		public void ScopedReadonlyRefShouldAllowDirectReadActionsOnTarget() {
			const int NumIterations = 200_000;

			var runner = _sixteenByteRunnerFactory.NewRunner(new SixteenVal(1, 2));

			var remainingReadThreads = new AtomicInt(0);
			AtomicVal<SixteenVal>.ScopedReadonlyRefToken persistedReadToken = default;
			runner.GlobalSetUp = (target, threadConfig) => {
				persistedReadToken = target.CreateScopedReadonlyRef();
				remainingReadThreads.Set(threadConfig.ReaderThreadCount);
				AssertReads(target);
			};
			runner.AllThreadsTearDown = target => Assert.AreEqual(new SixteenVal(3, 4), target.Value);
			runner.ExecuteSingleWriterTests(
				writerFunction: target => target.Set(new SixteenVal(3, 4)),
				readerFunction: target => {
					for (var i = 0; i < NumIterations; i++) {
						AssertReads(target);
					}

					if (remainingReadThreads.Decrement().NewValue == 0) persistedReadToken.Dispose();
				}
			);


			void AssertReads(AtomicVal<SixteenVal> target) {
				AssertAreEqual(new SixteenVal(1, 2), target.Value);
				AssertAreEqual(new SixteenVal(1, 2), target.Get());
				AssertAreEqual(new SixteenVal(1, 2), target.SpinWaitForValue(new SixteenVal(1, 2)));
			}
		}

		[Test]
		public void ScopedMutableRefShouldNotAllowConcurrentAccess() {
			var runner = _sixteenByteRunnerFactory.NewRunner(new SixteenVal(1, 2));

			var readThreadCount = new AtomicInt(0);
			AtomicVal<SixteenVal>.ScopedMutableRefToken persistedWriteToken = default;
			runner.GlobalSetUp = (target, threadConfig) => {
				persistedWriteToken = target.CreateScopedMutableRef();
				readThreadCount.Set(threadConfig.ReaderThreadCount);
			};
			runner.AllThreadsTearDown = target => Assert.AreEqual(new SixteenVal(3, 4), target.Value);
			runner.ExecuteSingleWriterTests(
				writerFunction: target => {
					Thread.Sleep(200);
					persistedWriteToken.Value = new SixteenVal(3, 4);
					persistedWriteToken.Dispose();
				},
				readerFunction: target => {
					var readRes = target.Value;
					AssertAreEqual(3, readRes.Alpha);
					AssertAreEqual(4, readRes.Bravo);
				}
			);

			runner.AllThreadsTearDown = target => Assert.AreEqual(new SixteenVal(3 + readThreadCount, 4 + readThreadCount), target.Value);
			runner.ExecuteSingleWriterTests(
				writerFunction: target => {
					Thread.Sleep(200);
					persistedWriteToken.Value = new SixteenVal(3, 4);
					persistedWriteToken.Dispose();
				},
				readerFunction: target => {
					var exchRes = target.Exchange(old => new SixteenVal(old.Alpha + 1, old.Bravo + 1));
					AssertTrue(3 <= exchRes.PreviousValue.Alpha);
					AssertTrue(4 <= exchRes.PreviousValue.Bravo);
				}
			);
		}
		#endregion
	}
}