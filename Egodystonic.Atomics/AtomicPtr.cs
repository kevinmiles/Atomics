﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Egodystonic.Atomics.Numerics;

namespace Egodystonic.Atomics {
	public sealed unsafe class AtomicPtr<T> : IAtomic<IntPtr> where T : unmanaged {
		public struct TypedPtrTryExchangeRes : IEquatable<TypedPtrTryExchangeRes> {
			public readonly bool ValueWasSet;
			public readonly T* PreviousValue;
			public readonly T* CurrentValue;

			public (bool ValueWasSet, IntPtr PreviousValue, IntPtr CurrentValue) AsUntyped => (ValueWasSet, (IntPtr) PreviousValue, (IntPtr) CurrentValue);

			public TypedPtrTryExchangeRes(bool valueWasSet, T* previousValue, T* currentValue) {
				ValueWasSet = valueWasSet;
				PreviousValue = previousValue;
				CurrentValue = currentValue;
			}

			public void Deconstruct(out bool valueWasSet, out T* previousValue, out T* currentValue) {
				valueWasSet = ValueWasSet;
				previousValue = PreviousValue;
				currentValue = CurrentValue;
			}

			public bool Equals(TypedPtrTryExchangeRes other) {
				return ValueWasSet == other.ValueWasSet && PreviousValue == other.PreviousValue && CurrentValue == other.CurrentValue;
			}

			public override bool Equals(object obj) {
				if (ReferenceEquals(null, obj)) return false;
				return obj is TypedPtrTryExchangeRes other && Equals(other);
			}

			public override int GetHashCode() {
				unchecked {
					var hashCode = ValueWasSet.GetHashCode();
					hashCode = (hashCode * 397) ^ unchecked((int) (long) PreviousValue);
					hashCode = (hashCode * 397) ^ unchecked((int) (long) CurrentValue);
					return hashCode;
				}
			}

			public static bool operator ==(TypedPtrTryExchangeRes left, TypedPtrTryExchangeRes right) { return left.Equals(right); }
			public static bool operator !=(TypedPtrTryExchangeRes left, TypedPtrTryExchangeRes right) { return !left.Equals(right); }
		}

		public struct TypedPtrExchangeRes : IEquatable<TypedPtrExchangeRes> {
			public readonly T* PreviousValue;
			public readonly T* CurrentValue;

			public (IntPtr PreviousValue, IntPtr CurrentValue) AsUntyped => ((IntPtr) PreviousValue, (IntPtr) CurrentValue);

			public TypedPtrExchangeRes(T* previousValue, T* currentValue) {
				PreviousValue = previousValue;
				CurrentValue = currentValue;
			}

			public bool Equals(TypedPtrExchangeRes other) {
				return PreviousValue == other.PreviousValue && CurrentValue == other.CurrentValue;
			}

			public override bool Equals(object obj) {
				if (ReferenceEquals(null, obj)) return false;
				return obj is TypedPtrExchangeRes other && Equals(other);
			}

			public override int GetHashCode() {
				unchecked {
					return (unchecked((int) (long) PreviousValue) * 397) ^ unchecked((int) (long) CurrentValue);
				}
			}

			public void Deconstruct(out T* previousValue, out T* currentValue) {
				previousValue = PreviousValue;
				currentValue = CurrentValue;
			}

			public static bool operator ==(TypedPtrExchangeRes left, TypedPtrExchangeRes right) { return left.Equals(right); }
			public static bool operator !=(TypedPtrExchangeRes left, TypedPtrExchangeRes right) { return !left.Equals(right); }
		}

		public delegate bool AtomicPtrPredicate(T* curValue, T* CurrentValue);
		public delegate bool AtomicPtrPredicate<in TContext>(T* curValue, T* CurrentValue, TContext context);
		public delegate T* AtomicPtrMap(T* curValue);
		public delegate T* AtomicPtrMap<in TContext>(T* curValue, TContext context);

		IntPtr _value;

		public AtomicPtr() : this(default(IntPtr)) { }
		public AtomicPtr(IntPtr initialValue) => _value = initialValue;
		public AtomicPtr(T* initialValue) => _value = (IntPtr) initialValue;

		public T* Value {
			[MethodImpl(MethodImplOptions.AggressiveInlining)] get => Get();
			[MethodImpl(MethodImplOptions.AggressiveInlining)] set => Set(value);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T* Get() => (T*) Volatile.Read(ref _value);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public IntPtr GetAsIntPtr() => Volatile.Read(ref _value);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T* GetUnsafe() => (T*) _value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Set(T* CurrentValue) => Volatile.Write(ref _value, (IntPtr) CurrentValue);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetUnsafe(T* CurrentValue) => _value = (IntPtr) CurrentValue;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetAsIntPtr(IntPtr CurrentValue) => Volatile.Write(ref _value, CurrentValue);

		public TypedPtrExchangeRes Exchange(T* CurrentValue) => new TypedPtrExchangeRes((T*) Interlocked.Exchange(ref _value, (IntPtr) CurrentValue), CurrentValue);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T* SpinWaitForValue(T* targetValue) {
			var spinner = new SpinWait();
			while (Get() != targetValue) spinner.SpinOnce();
			return targetValue;
		}

		public TypedPtrExchangeRes Exchange<TContext>(AtomicPtrMap<TContext> mapFunc, TContext context) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = Get();
				var CurrentValue = mapFunc(curValue, context);

				if (Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) curValue) == (IntPtr) curValue) return new TypedPtrExchangeRes(curValue, CurrentValue);
				spinner.SpinOnce();
			}
		}

		public TypedPtrExchangeRes SpinWaitForExchange(T* CurrentValue, T* comparand) {
			var spinner = new SpinWait();

			while (true) {
				if (Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) comparand) == (IntPtr) comparand) return new TypedPtrExchangeRes(comparand, CurrentValue);
				spinner.SpinOnce();
			}
		}

		public TypedPtrExchangeRes SpinWaitForExchange<TContext>(AtomicPtrMap<TContext> mapFunc, T* comparand, TContext context) {
			var spinner = new SpinWait();
			var CurrentValue = mapFunc(comparand, context); // curValue will always be comparand when this method returns

			while (true) {
				if (Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) comparand) == (IntPtr) comparand) return new TypedPtrExchangeRes(comparand, CurrentValue);
				spinner.SpinOnce();
			}
		}

		public TypedPtrExchangeRes SpinWaitForExchange<TMapContext, TPredicateContext>(AtomicPtrMap<TMapContext> mapFunc, AtomicPtrPredicate<TPredicateContext> predicate, TMapContext mapContext, TPredicateContext predicateContext) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = Get();
				var CurrentValue = mapFunc(curValue, mapContext);
				if (!predicate(curValue, CurrentValue, predicateContext)) {
					spinner.SpinOnce();
					continue;
				}

				if (Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) curValue) == (IntPtr) curValue) return new TypedPtrExchangeRes(curValue, CurrentValue);
				spinner.SpinOnce();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrTryExchangeRes TryExchange(T* CurrentValue, T* comparand) {
			var oldValue = (T*) Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) comparand);
			var wasSet = oldValue == comparand;
			return new TypedPtrTryExchangeRes(wasSet, oldValue, wasSet ? CurrentValue : oldValue);
		}

		public TypedPtrTryExchangeRes TryExchange<TContext>(AtomicPtrMap<TContext> mapFunc, T* comparand, TContext context) {
			var CurrentValue = mapFunc(comparand, context); // Comparand will always be curValue if the interlocked call passes
			var prevValue = (T*) Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) comparand);
			if (prevValue == comparand) return new TypedPtrTryExchangeRes(true, prevValue, CurrentValue);
			else return new TypedPtrTryExchangeRes(false, prevValue, prevValue);
		}

		public TypedPtrTryExchangeRes TryExchange<TMapContext, TPredicateContext>(AtomicPtrMap<TMapContext> mapFunc, AtomicPtrPredicate<TPredicateContext> predicate, TMapContext mapContext, TPredicateContext predicateContext) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = Get();
				var CurrentValue = mapFunc(curValue, mapContext);
				if (!predicate(curValue, CurrentValue, predicateContext)) return new TypedPtrTryExchangeRes(false, curValue, curValue);

				if ((T*) Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) curValue) == curValue) return new TypedPtrTryExchangeRes(true, curValue, CurrentValue);

				spinner.SpinOnce();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrExchangeRes Increment() => Add(new IntPtr(1L));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrExchangeRes Decrement() => Subtract(new IntPtr(1L));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrExchangeRes Add(int operand) => Add(new IntPtr(operand));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrExchangeRes Add(long operand) => Add(new IntPtr(operand));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrExchangeRes Subtract(int operand) => Subtract(new IntPtr(operand));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrExchangeRes Subtract(long operand) => Subtract(new IntPtr(operand));

		public TypedPtrExchangeRes Add(IntPtr operand) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = Get();
				var CurrentValue = curValue + operand.ToInt64();
				var oldValue = (T*) Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) curValue);
				if (oldValue == curValue) return new TypedPtrExchangeRes(oldValue, CurrentValue);
				spinner.SpinOnce();
			}
		}

		public TypedPtrExchangeRes Subtract(IntPtr operand) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = Get();
				var CurrentValue = curValue - operand.ToInt64();
				var oldValue = (T*) Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) curValue);
				if (oldValue == curValue) return new TypedPtrExchangeRes(oldValue, CurrentValue);
				spinner.SpinOnce();
			}
		}

		// ============================ IAtomic<IntPtr> API ============================

		IntPtr IAtomic<IntPtr>.Value {
			[MethodImpl(MethodImplOptions.AggressiveInlining)] get => (IntPtr) Value;
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			set => Value = (T*) value;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)] IntPtr IAtomic<IntPtr>.Get() => GetAsIntPtr();
		[MethodImpl(MethodImplOptions.AggressiveInlining)] IntPtr IAtomic<IntPtr>.GetUnsafe() => (IntPtr) GetUnsafe();
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public void Set(IntPtr CurrentValue) => SetAsIntPtr(CurrentValue);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetUnsafe(IntPtr CurrentValue) => SetUnsafe((T*) CurrentValue);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] (IntPtr PreviousValue, IntPtr CurrentValue) IAtomic<IntPtr>.Exchange(IntPtr CurrentValue) => Exchange((T*) CurrentValue).AsUntyped;

		[MethodImpl(MethodImplOptions.AggressiveInlining)] IntPtr IAtomic<IntPtr>.SpinWaitForValue(IntPtr targetValue) => (IntPtr) SpinWaitForValue((T*) targetValue);

		(IntPtr PreviousValue, IntPtr CurrentValue) IAtomic<IntPtr>.Exchange<TContext>(Func<IntPtr, TContext, IntPtr> mapFunc, TContext context) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = GetAsIntPtr();
				var CurrentValue = mapFunc(curValue, context);

				if (Interlocked.CompareExchange(ref _value, CurrentValue, curValue) == curValue) return (curValue, CurrentValue);
				spinner.SpinOnce();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)] (IntPtr PreviousValue, IntPtr CurrentValue) IAtomic<IntPtr>.SpinWaitForExchange(IntPtr CurrentValue, IntPtr comparand) => SpinWaitForExchange((T*) CurrentValue, (T*) comparand).AsUntyped;

		(IntPtr PreviousValue, IntPtr CurrentValue) IAtomic<IntPtr>.SpinWaitForExchange<TContext>(Func<IntPtr, TContext, IntPtr> mapFunc, TContext context, IntPtr comparand) {
			var spinner = new SpinWait();
			var CurrentValue = mapFunc(comparand, context); // curValue will always be comparand when this method returns

			while (true) {
				if (Interlocked.CompareExchange(ref _value, CurrentValue, comparand) == comparand) return (comparand, CurrentValue);
				spinner.SpinOnce();
			}
		}

		(IntPtr PreviousValue, IntPtr CurrentValue) IAtomic<IntPtr>.SpinWaitForExchange<TMapContext, TPredicateContext>(Func<IntPtr, TMapContext, IntPtr> mapFunc, TMapContext mapContext, Func<IntPtr, IntPtr, TPredicateContext, bool> predicate, TPredicateContext predicateContext) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = GetAsIntPtr();
				var CurrentValue = mapFunc(curValue, mapContext);
				if (!predicate(curValue, CurrentValue, predicateContext)) {
					spinner.SpinOnce();
					continue;
				}

				if (Interlocked.CompareExchange(ref _value, CurrentValue, curValue) == curValue) return (curValue, CurrentValue);
				spinner.SpinOnce();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)] (bool ValueWasSet, IntPtr PreviousValue, IntPtr CurrentValue) IAtomic<IntPtr>.TryExchange(IntPtr CurrentValue, IntPtr comparand) => TryExchange((T*) CurrentValue, (T*) comparand).AsUntyped;

		(bool ValueWasSet, IntPtr PreviousValue, IntPtr CurrentValue) IAtomic<IntPtr>.TryExchange<TContext>(Func<IntPtr, TContext, IntPtr> mapFunc, TContext context, IntPtr comparand) {
			var CurrentValue = mapFunc(comparand, context); // Comparand will always be curValue if the interlocked call passes
			var prevValue = Interlocked.CompareExchange(ref _value, CurrentValue, comparand);
			if (prevValue == comparand) return (true, prevValue, CurrentValue);
			else return (false, prevValue, prevValue);
		}

		(bool ValueWasSet, IntPtr PreviousValue, IntPtr CurrentValue) IAtomic<IntPtr>.TryExchange<TMapContext, TPredicateContext>(Func<IntPtr, TMapContext, IntPtr> mapFunc, TMapContext mapContext, Func<IntPtr, IntPtr, TPredicateContext, bool> predicate, TPredicateContext predicateContext) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = GetAsIntPtr();
				var CurrentValue = mapFunc(curValue, mapContext);
				if (!predicate(curValue, CurrentValue, predicateContext)) return (false, curValue, curValue);

				if (Interlocked.CompareExchange(ref _value, CurrentValue, curValue) == curValue) return (true, curValue, CurrentValue);

				spinner.SpinOnce();
			}
		}

		// ============================ Missing extension methods for IAtomic<T*> ============================

		public delegate bool AtomicPtrSpinWaitPredicate(T* curValue);
		public delegate bool AtomicPtrSpinWaitPredicate<in TContext>(T* curValue, TContext context);

		public T* SpinWaitForValue(AtomicPtrSpinWaitPredicate predicate) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = Get();
				if (predicate(curValue)) return curValue;
				spinner.SpinOnce();
			}
		}

		public T* SpinWaitForValue<TContext>(AtomicPtrSpinWaitPredicate<TContext> predicate, TContext context) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = Get();
				if (predicate(curValue, context)) return curValue;
				spinner.SpinOnce();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrExchangeRes Exchange(AtomicPtrMap mapFunc) => Exchange((curVal, ctx) => ctx(curVal), mapFunc);

		public TypedPtrExchangeRes SpinWaitForExchange(T* CurrentValue, AtomicPtrPredicate predicate) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = Get();
				if (!predicate(curValue, CurrentValue)) {
					spinner.SpinOnce();
					continue;
				}

				if (Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) curValue) == (IntPtr) curValue) return new TypedPtrExchangeRes(curValue, CurrentValue);
				spinner.SpinOnce();
			}
		}

		public TypedPtrExchangeRes SpinWaitForExchange<TContext>(T* CurrentValue, AtomicPtrPredicate<TContext> predicate, TContext context) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = Get();
				if (!predicate(curValue, CurrentValue, context)) {
					spinner.SpinOnce();
					continue;
				}

				if (Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) curValue) == (IntPtr) curValue) return new TypedPtrExchangeRes(curValue, CurrentValue);
				spinner.SpinOnce();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrExchangeRes SpinWaitForExchange(AtomicPtrMap mapFunc, T* comparand) {
			return SpinWaitForExchange((curVal, ctx) => ctx(curVal), comparand, mapFunc);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrExchangeRes SpinWaitForExchange(AtomicPtrMap mapFunc, AtomicPtrPredicate predicate) {
			return SpinWaitForExchange((curVal, ctx) => ctx(curVal), (curVal, newVal, ctx) => ctx(curVal, newVal), mapFunc, predicate);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrExchangeRes SpinWaitForExchange<TContext>(AtomicPtrMap<TContext> mapFunc, AtomicPtrPredicate predicate, TContext context) {
			return SpinWaitForExchange(mapFunc, (curVal, newVal, ctx) => ctx(curVal, newVal), context, predicate);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrExchangeRes SpinWaitForExchange<TContext>(AtomicPtrMap mapFunc, AtomicPtrPredicate<TContext> predicate, TContext context) {
			return SpinWaitForExchange((curVal, ctx) => ctx(curVal), predicate, mapFunc, context);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrExchangeRes SpinWaitForExchange<TContext>(AtomicPtrMap<TContext> mapFunc, AtomicPtrPredicate<TContext> predicate, TContext context) {
			return SpinWaitForExchange(mapFunc, predicate, context, context);
		}

		public TypedPtrTryExchangeRes TryExchange(T* CurrentValue, AtomicPtrPredicate predicate) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = Get();
				if (!predicate(curValue, CurrentValue)) return new TypedPtrTryExchangeRes(false, curValue, curValue);

				if ((T*) Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) curValue) == curValue) return new TypedPtrTryExchangeRes(true, curValue, CurrentValue);

				spinner.SpinOnce();
			}
		}

		public TypedPtrTryExchangeRes TryExchange<TContext>(T* CurrentValue, AtomicPtrPredicate<TContext> predicate, TContext context) {
			var spinner = new SpinWait();

			while (true) {
				var curValue = Get();
				if (!predicate(curValue, CurrentValue, context)) return new TypedPtrTryExchangeRes(false, curValue, curValue);

				if ((T*) Interlocked.CompareExchange(ref _value, (IntPtr) CurrentValue, (IntPtr) curValue) == curValue) return new TypedPtrTryExchangeRes(true, curValue, CurrentValue);

				spinner.SpinOnce();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrTryExchangeRes TryExchange(AtomicPtrMap mapFunc, T* comparand) {
			return TryExchange((curVal, ctx) => ctx(curVal), comparand, mapFunc);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrTryExchangeRes TryExchange(AtomicPtrMap mapFunc, AtomicPtrPredicate predicate) {
			return TryExchange((curVal, ctx) => ctx(curVal), (curVal, newVal, ctx) => ctx(curVal, newVal), mapFunc, predicate);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrTryExchangeRes TryExchange<TContext>(AtomicPtrMap<TContext> mapFunc, AtomicPtrPredicate predicate, TContext context) {
			return TryExchange(mapFunc, (curVal, newVal, ctx) => ctx(curVal, newVal), context, predicate);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrTryExchangeRes TryExchange<TContext>(AtomicPtrMap mapFunc, AtomicPtrPredicate<TContext> predicate, TContext context) {
			return TryExchange((curVal, ctx) => ctx(curVal), predicate, mapFunc, context);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TypedPtrTryExchangeRes TryExchange<TContext>(AtomicPtrMap<TContext> mapFunc, AtomicPtrPredicate<TContext> predicate, TContext context) {
			return TryExchange(mapFunc, predicate, context, context);
		}
	}
}
