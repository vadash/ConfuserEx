﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Confuser.Core.Services;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.DependencyInjection;

namespace Confuser.Helpers {
	public delegate IReadOnlyList<Instruction> PlaceholderProcessor(IReadOnlyList<Instruction> arguments);

	public delegate IReadOnlyList<Instruction> CryptProcessor(MethodDef method, Local block, Local key);

	public class MutationProcessor : IMethodInjectProcessor {
		private const string MutationClassName = "Mutation";

		private TypeDef MutationTypeDef { get; }
		private ITraceService TraceService { get; }
		public IReadOnlyDictionary<MutationField, int> KeyFieldValues { get; set; }
		public PlaceholderProcessor PlaceholderProcessor { get; set; }
		public CryptProcessor CryptProcessor { get; set; }

		public MutationProcessor(IServiceProvider services) {
			if (services == null) throw new ArgumentNullException(nameof(services));

			var runtimeService = services.GetRequiredService<IRuntimeService>();
			TraceService = services.GetRequiredService<ITraceService>();

			MutationTypeDef = runtimeService.GetRuntimeType(MutationClassName);
		}

		void IMethodInjectProcessor.Process(MethodDef method) {
			Debug.Assert(method != null, $"{nameof(method)} != null");

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode == OpCodes.Ldsfld) {
					if (instr.Operand is IField loadedField && loadedField.DeclaringType == MutationTypeDef) {
						if (!ProcessKeyField(instr, loadedField))
							throw new InvalidOperationException("Unexpected load field operation to Mutation class!");
					}
				}
				else if (instr.OpCode == OpCodes.Call) {
					if (instr.Operand is IMethod calledMethod && calledMethod.DeclaringType == MutationTypeDef) {
						if (!ReplacePlaceholder(method, instr, calledMethod) && !ReplaceCrypt(method, instr, calledMethod))
							throw new InvalidOperationException("Unexpected call operation to Mutation class!");
					}
				}
			}
			throw new NotImplementedException();
		}

		private bool ProcessKeyField(Instruction instr, IField field) {
			Debug.Assert(instr != null, $"{nameof(instr)} != null");
			Debug.Assert(field != null, $"{nameof(field)} != null");

			if (field.Name?.Length >= 5 && field.Name.StartsWith("KeyI")) {
				var number = field.Name.String.AsSpan().Slice(start: 4, length: (field.Name.Length == 5 ? 1 : 2));
				if (int.TryParse(number.ToString(), out var value)) {
					MutationField mutationField;
					switch (value) {
						case 0: mutationField = MutationField.KeyI0; break;
						case 1: mutationField = MutationField.KeyI1; break;
						case 2: mutationField = MutationField.KeyI2; break;
						case 3: mutationField = MutationField.KeyI3; break;
						case 4: mutationField = MutationField.KeyI4; break;
						case 5: mutationField = MutationField.KeyI5; break;
						case 6: mutationField = MutationField.KeyI6; break;
						case 7: mutationField = MutationField.KeyI7; break;
						case 8: mutationField = MutationField.KeyI8; break;
						case 9: mutationField = MutationField.KeyI9; break;
						case 10: mutationField = MutationField.KeyI10; break;
						case 11: mutationField = MutationField.KeyI11; break;
						case 12: mutationField = MutationField.KeyI12; break;
						case 13: mutationField = MutationField.KeyI13; break;
						case 14: mutationField = MutationField.KeyI14; break;
						case 15: mutationField = MutationField.KeyI15; break;
						default: return false;
					}

					if (KeyFieldValues.TryGetValue(mutationField, out var keyValue)) {
						instr.OpCode = OpCodes.Ldc_I4;
						instr.Operand = keyValue;
						return true;
					}
					else {
						throw new InvalidOperationException($"Code contains request to mutation key {field.Name}, but the value for this field is not set.");
					}
				}
			}

			return false;
		}

		private bool ReplacePlaceholder(MethodDef method, Instruction instr, IMethod calledMethod) {
			Debug.Assert(method != null, $"{nameof(method)} != null");
			Debug.Assert(instr != null, $"{nameof(instr)} != null");
			Debug.Assert(calledMethod != null, $"{nameof(calledMethod)} != null");

			if (calledMethod.Name == "Placeholder") {
				if (PlaceholderProcessor == null) throw new InvalidOperationException("Found mutation placeholder, but there is no processor defined.");
				var trace = TraceService.Trace(method);
				int[] argIndexes = trace.TraceArguments(instr);
				if (argIndexes == null) throw new InvalidOperationException("Failed to trace placeholder argument.");

				int argIndex = argIndexes[0];
				IReadOnlyList<Instruction> arg = method.Body.Instructions.Skip(argIndex).TakeWhile(i => i != instr).ToImmutableArray();

				for (int j = 0; j < arg.Count; j++)
					method.Body.Instructions.RemoveAt(argIndex);
				method.Body.Instructions.RemoveAt(argIndex);

				arg = PlaceholderProcessor(arg);
				for (int j = arg.Count - 1; j >= 0; j--)
					method.Body.Instructions.Insert(argIndex, arg[j]);

				return true;
			}

			return false;
		}

		private bool ReplaceCrypt(MethodDef method, Instruction instr, IMethod calledMethod) {
			Debug.Assert(method != null, $"{nameof(method)} != null");
			Debug.Assert(instr != null, $"{nameof(instr)} != null");
			Debug.Assert(calledMethod != null, $"{nameof(calledMethod)} != null");

			if (calledMethod.Name == "Crypt") {
				if (CryptProcessor == null) throw new InvalidOperationException("Found mutation crypt, but not processor defined.");

				var instrIndex = method.Body.Instructions.IndexOf(instr);
				var ldBlock = method.Body.Instructions[instrIndex - 2];
				var ldKey = method.Body.Instructions[instrIndex - 1];
				Debug.Assert(ldBlock.OpCode == OpCodes.Ldloc && ldKey.OpCode == OpCodes.Ldloc);

				method.Body.Instructions.RemoveAt(instrIndex);
				method.Body.Instructions.RemoveAt(instrIndex - 1);
				method.Body.Instructions.RemoveAt(instrIndex - 2);

				var cryptInstr = CryptProcessor(method, (Local)ldBlock.Operand, (Local)ldKey.Operand);
				for (var i = 0; i< cryptInstr.Count; i++) {
					method.Body.Instructions.Insert(instrIndex - 2 + i, cryptInstr[i]);
				}

				return true;
			}
			return false;
		}
	}
}
