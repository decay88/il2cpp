﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using dnlib.DotNet;

namespace il2cpp
{
	// 泛型签名替换器
	internal class GenericReplacer
	{
		public TypeX OwnerTypeX;
		public MethodX OwnerMethodX;

		public TypeSig Replace(GenericVar genVarSig)
		{
			if (genVarSig.OwnerType.FullName == OwnerTypeX.DefFullName)
				return OwnerTypeX.GenArgs[(int)genVarSig.Number];
			return genVarSig;
		}

		public TypeSig Replace(GenericMVar genMVarSig)
		{
			if (genMVarSig.OwnerMethod.FullName == OwnerMethodX.DefFullName)
				return OwnerMethodX.GenArgs[(int)genMVarSig.Number];
			return genMVarSig;
		}
	}

	// 类型管理器
	internal class TypeManager
	{
		// 当前环境
		private readonly Il2cppContext Context;

		// 实例类型映射
		private readonly Dictionary<string, TypeX> TypeMap = new Dictionary<string, TypeX>();

		internal TypeManager(Il2cppContext context)
		{
			Context = context;
		}

		// 添加待处理的方法
		public void AddPendingMethod(MethodDef metDef)
		{

		}

		// 解析所有引用
		public void ResolveAll()
		{

		}

		public MethodX ResolveMethodDef(MethodDef metDef)
		{
			MethodX metX = ResolveMethodDefImpl(metDef);
			return AddMethod(metX);
		}

		private MethodX ResolveMethodDefImpl(MethodDef metDef)
		{
			TypeX declType = ResolveITypeDefOrRef(metDef.DeclaringType, null);
			return new MethodX(declType, metDef);
		}

		private MethodX AddMethod(MethodX metX)
		{
			Debug.Assert(metX != null);

			string nameKey = metX.GetNameKey();
			if (metX.DeclType.TryGetMethod(nameKey, out metX))
				return metX;

			metX.DeclType.AddMethod(nameKey, metX);

			//! expand method

			return metX;
		}

		public TypeX ResolveITypeDefOrRef(ITypeDefOrRef tyDefRef, GenericReplacer replacer)
		{
			TypeX tyX = ResolveITypeDefOrRefImpl(tyDefRef, replacer);
			Debug.Assert(tyX != null);

			string nameKey = tyX.GetNameKey();
			if (TypeMap.TryGetValue(nameKey, out tyX))
				return tyX;

			TypeMap.Add(nameKey, tyX);

			//! expandtype

			return tyX;
		}

		private TypeX ResolveITypeDefOrRefImpl(ITypeDefOrRef tyDefRef, GenericReplacer replacer)
		{
			switch (tyDefRef)
			{
				case TypeDef tyDef:
					return new TypeX(Context, CorrectTypeDefVersion(tyDef));

				case TypeRef tyRef:
					{
						TypeDef tyDef = tyRef.Resolve();
						if (tyDef != null)
							return new TypeX(Context, CorrectTypeDefVersion(tyDef));

						throw new NotSupportedException();
					}

				case TypeSpec tySpec:
					return ResolveTypeSig(tySpec.TypeSig, replacer);

				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private TypeX ResolveTypeSig(TypeSig tySig, GenericReplacer replacer)
		{
			switch (tySig)
			{
				case TypeDefOrRefSig tyDefRefSig:
					return ResolveITypeDefOrRefImpl(tyDefRefSig.TypeDefOrRef, null);

				case GenericInstSig genInstSig:
					{
						TypeX genType = ResolveITypeDefOrRefImpl(genInstSig.GenericType.TypeDefOrRef, null);
						genType.GenArgs = ReplaceGenericSigList(genInstSig.GenericArguments, replacer);
						return genType;
					}

				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private TypeDef CorrectTypeDefVersion(TypeDef tyDef)
		{
			if (tyDef.Module.RuntimeVersion == Context.RuntimeVersion)
				return tyDef;

			TypeRef tyRef = Context.Module.CorLibTypes.GetTypeRef(tyDef.Namespace, tyDef.Name);
			Debug.Assert(tyRef != null);

			return tyRef.Resolve();
		}

		// 替换类型中的泛型签名
		private static TypeSig ReplaceGenericSig(TypeSig tySig, GenericReplacer replacer)
		{
			Debug.Assert(replacer != null);

			if (tySig == null)
				return null;

			if (!IsGenericReplaceNeeded(tySig))
				return tySig;

			switch (tySig.ElementType)
			{
				case ElementType.Class:
				case ElementType.ValueType:
					return tySig;

				case ElementType.Ptr:
					return new PtrSig(ReplaceGenericSig(tySig.Next, replacer));
				case ElementType.ByRef:
					return new ByRefSig(ReplaceGenericSig(tySig.Next, replacer));
				case ElementType.Pinned:
					return new PinnedSig(ReplaceGenericSig(tySig.Next, replacer));
				case ElementType.SZArray:
					return new SZArraySig(ReplaceGenericSig(tySig.Next, replacer));

				case ElementType.Array:
					{
						ArraySig arySig = (ArraySig)tySig;
						return new ArraySig(ReplaceGenericSig(arySig.Next, replacer),
							arySig.Rank,
							arySig.Sizes,
							arySig.LowerBounds);
					}
				case ElementType.CModReqd:
					{
						CModReqdSig modreqdSig = (CModReqdSig)tySig;
						return new CModReqdSig(modreqdSig.Modifier, ReplaceGenericSig(modreqdSig.Next, replacer));
					}
				case ElementType.CModOpt:
					{
						CModOptSig modoptSig = (CModOptSig)tySig;
						return new CModOptSig(modoptSig.Modifier, ReplaceGenericSig(modoptSig.Next, replacer));
					}
				case ElementType.GenericInst:
					{
						GenericInstSig genInstSig = (GenericInstSig)tySig;
						return new GenericInstSig(genInstSig.GenericType, ReplaceGenericSigList(genInstSig.GenericArguments, replacer));
					}

				case ElementType.Var:
					{
						GenericVar genVarSig = (GenericVar)tySig;
						TypeSig result = replacer.Replace(genVarSig);
						if (result != null)
							return result;
						return new GenericVar(genVarSig.Number, genVarSig.OwnerType);
					}
				case ElementType.MVar:
					{
						GenericMVar genMVarSig = (GenericMVar)tySig;
						TypeSig result = replacer.Replace(genMVarSig);
						if (result != null)
							return result;
						return new GenericMVar(genMVarSig.Number, genMVarSig.OwnerMethod);
					}

				default:
					if (tySig is CorLibTypeSig)
						return tySig;

					throw new NotSupportedException();
			}
		}

		// 替换类型签名列表
		private static IList<TypeSig> ReplaceGenericSigList(IList<TypeSig> tySigList, GenericReplacer replacer)
		{
			return tySigList?.Select(tySig => ReplaceGenericSig(tySig, replacer)).ToList();
		}

		// 是否存在要替换的泛型签名
		private static bool IsGenericReplaceNeeded(TypeSig tySig)
		{
			while (tySig != null)
			{
				switch (tySig.ElementType)
				{
					case ElementType.Var:
					case ElementType.MVar:
						return true;

					case ElementType.GenericInst:
						{
							GenericInstSig genInstSig = (GenericInstSig)tySig;
							foreach (var arg in genInstSig.GenericArguments)
							{
								if (IsGenericReplaceNeeded(arg))
									return true;
							}
						}
						break;
				}

				tySig = tySig.Next;
			}
			return false;
		}
	}
}