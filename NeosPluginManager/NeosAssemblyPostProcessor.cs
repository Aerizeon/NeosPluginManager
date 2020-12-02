using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using Mono.Cecil.Rocks;
using PostX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NeosPluginManager
{
    /// <summary>
    /// This class contains black magic imported directly from Neos
    /// </summary>
    public static class NeosAssemblyPostProcessor
    {
        public const string PROCESSED_LABEL = "POSTX_PROCESSED";
        public const string WORKER_TYPENAME = "FrooxEngine.Worker";
        public const string ISYNCMEMBER_TYPENAME = "FrooxEngine.ISyncMember";
        public const string LOGIX_NODE_TYPENAME = "FrooxEngine.LogiX.LogixNode";
        public const string IOUTPUTELEMENT_TYPENAME = "FrooxEngine.LogiX.IOutputElement";
        public const string SYNCOBJECT_TYPENAME = "FrooxEngine.SyncObject";
        public const string SYNCVAR_TYPENAME = "FrooxEngine.SyncVar";
        public const string CODER_TYPENAME = "FrooxEngine.Coder`1";
        public const string CODER_NULLABLE_TYPENAME = "FrooxEngine.CoderNullable`1";
        public const string ISYNCLIST_TYPENAME = "FrooxEngine.ISyncList";
        public const string ISYNCDICTIONARY_TYPENAME = "FrooxEngine.ISyncDictionary";

        public static bool Process(string path, string frooxEngineModuleRoot = null)
        {
            DefaultAssemblyResolver assemblyResolver = new DefaultAssemblyResolver();
            assemblyResolver.AddSearchDirectory(Path.GetDirectoryName(path));
            bool flag1 = File.Exists(Path.ChangeExtension(path, ".pdb"));
            AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(path, new ReaderParameters()
            {
                AssemblyResolver = assemblyResolver,
                ReadSymbols = flag1,
                SymbolReaderProvider = flag1 ? new PdbReaderProvider() : null,
                ReadWrite = true
            });

            ModuleDefinition ImportModle = assemblyDefinition.MainModule;
            ModuleDefinition FrooxEngineModule = ModuleDefinition.ReadModule(Path.Combine(frooxEngineModuleRoot ?? Path.GetDirectoryName(path), "FrooxEngine.dll"));

            TypeReference returnType1 = ImportModle.ImportReference(FrooxEngineModule.GetType("FrooxEngine.ISyncMember", true));
            TypeDefinition type1 = ImportModle.ImportReference(FrooxEngineModule.GetType("FrooxEngine.LogiX.IOutputElement", true)).Resolve();
            TypeReference returnType2 = ImportModle.ImportReference(typeof(void));
            TypeReference parameterType = ImportModle.ImportReference(typeof(int));
            TypeReference type2 = ImportModle.ImportReference(typeof(string));
            MethodReference constructor1 = GetConstructor(ImportModle.ImportReference(typeof(ArgumentOutOfRangeException)).Resolve(), ImportModle, out bool _);

            List<TypeDefinition> list1 = new List<TypeDefinition>();
            MethodDefinition methodDefinition1 = null;
            MethodDefinition methodDefinition2 = null;
            TypeDefinition self1 = FrooxEngineModule.GetType("FrooxEngine.Coder`1", true).Resolve();
            MethodDefinition self2 = self1.Methods.FirstOrDefault(m => m.Name == "Dummy");
            TypeDefinition self3 = FrooxEngineModule.GetType("FrooxEngine.CoderNullable`1", true).Resolve();
            MethodDefinition self4 = self1.Methods.FirstOrDefault(m => m.Name == "Dummy");
            foreach (TypeDefinition type3 in ImportModle.Types)
            {
                if (type3.Name.Contains("CoderAOT"))
                {
                    methodDefinition1 = type3.Methods.FirstOrDefault(m => m.Name == "Dummy");
                    methodDefinition2 = type3.Methods.FirstOrDefault(m => m.Name == "AOT");
                }
                CollectWorkerTypes(type3, list1);
            }
            ILProcessor ilProcessor1 = methodDefinition1?.Body.GetILProcessor();
            ilProcessor1?.Body.Instructions.Clear();
            List<TypeDefinition> typeDefinitionList = new List<TypeDefinition>();
            HashSet<TypeReference> typeReferenceSet = new HashSet<TypeReference>();
            foreach (TypeDefinition typeDefinition2 in list1)
            {
                if (typeDefinition2.HasGenericParameters && typeDefinition2.GenericParameters.Count == 1 && (!typeDefinition2.GenericParameters[0].HasConstraints || IsNeosEnumConstraint(typeDefinition2)) && !typeDefinition2.GenericParameters[0].HasReferenceTypeConstraint)
                    typeDefinitionList.Add(typeDefinition2);
                MethodDefinition methodDefinition3 = new MethodDefinition("InitializeSyncMembers", Mono.Cecil.MethodAttributes.Family | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.HideBySig, returnType2);
                typeDefinition2.Methods.Add(methodDefinition3);
                if (!typeDefinition2.IsAbstract)
                {
                    MethodDefinition methodDefinition4 = new MethodDefinition("GetSyncMember", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.HideBySig, returnType1);
                    methodDefinition4.Parameters.Add(new ParameterDefinition("index", Mono.Cecil.ParameterAttributes.None, parameterType));
                    typeDefinition2.Methods.Add(methodDefinition4);
                    if (!(typeDefinition2.Name == "Slot"))
                    {
                        TypeReference typeReference = !typeDefinition2.HasGenericParameters ? typeDefinition2 : (TypeReference)typeDefinition2.MakeGenericInstanceType(typeDefinition2.GenericParameters.ToArray());
                        MethodDefinition methodDefinition5 = new MethodDefinition("__New", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, typeReference);
                        typeDefinition2.Methods.Add(methodDefinition5);
                        ILProcessor ilProcessor2 = methodDefinition5.Body.GetILProcessor();
                        MethodReference constructor2 = GetConstructor(typeReference, ImportModle, out bool isMethodCall);
                        if (constructor2 == null)
                            throw new Exception(string.Format("Worker {0} has no basic constructor", (object)typeDefinition2));
                        if (isMethodCall)
                            ilProcessor2.Emit(OpCodes.Call, constructor2);
                        else
                            ilProcessor2.Emit(OpCodes.Newobj, constructor2);
                        ilProcessor2.Emit(OpCodes.Ret);
                    }
                }
            }
            HashSet<TypeDefinition> warningWorkers = new HashSet<TypeDefinition>();
            foreach (TypeDefinition typeDefinition2 in list1)
            {
                MethodDefinition methodDefinition3 = typeDefinition2.Methods.First(m => m.Name.EndsWith("InitializeSyncMembers"));
                methodDefinition3.Body.InitLocals = true;
                ILProcessor ilProcessor2 = methodDefinition3.Body.GetILProcessor();
                if (typeDefinition2.BaseType.FullName != "FrooxEngine.Worker")
                {
                    TypeDefinition typeDefinition3 = typeDefinition2.BaseType.Resolve();
                    MethodDefinition self5 = typeDefinition3.Methods.FirstOrDefault(m => m.Name.EndsWith("InitializeSyncMembers"));
                    if (self5 == null)
                        throw new Exception(string.Format("Could not find InitializeSyncMembers on {0}, with baseType: {1}. ", typeDefinition2.Name, typeDefinition3) + "Available Methods: " + string.Join(", ", typeDefinition3.Methods.Select((m => m.Name))));
                    MethodReference method = ImportModle.ImportReference(self5, typeDefinition2);
                    if (typeDefinition2.BaseType is GenericInstanceType baseType)
                        method = self5.MakeHostInstanceGeneric(baseType);
                    ilProcessor2.Emit(OpCodes.Ldarg_0);
                    ilProcessor2.Emit(OpCodes.Call, method);
                }
                foreach (FieldDefinition field in typeDefinition2.Fields.ToList())
                {
                    if (!typeDefinition2.IsAbstract && field.FieldType.Name == "Sync`1")
                    {
                        TypeReference genericArgument = ((GenericInstanceType)field.FieldType).GenericArguments[0];
                        if (!genericArgument.IsGenericParameter)
                            typeReferenceSet.Add(genericArgument);
                    }
                    if (ShouldProcessField(field, typeDefinition2, warningWorkers))
                    {
                        field.Attributes &= ~Mono.Cecil.FieldAttributes.Private;
                        field.Attributes |= Mono.Cecil.FieldAttributes.Family;
                        MethodReference constructor2 = GetConstructor(field.FieldType, ImportModle, out bool isMethodCall);
                        TypeDefinition type3 = field.FieldType.Resolve();
                        FieldReference genericFieldReference = field.GetGenericFieldReference();
                        ilProcessor2.Emit(OpCodes.Ldarg_0);
                        if (isMethodCall)
                            ilProcessor2.Emit(OpCodes.Call, ImportModle.ImportReference(constructor2, typeDefinition2));
                        else
                            ilProcessor2.Emit(OpCodes.Newobj, ImportModle.ImportReference(constructor2, typeDefinition2));
                        ilProcessor2.Emit(OpCodes.Stfld, genericFieldReference);
                        if (field.CustomAttributes.Any(attr => attr.AttributeType.Name.EndsWith("NonPersistent")))
                        {
                            MethodDefinition methodDefinition4 = type3.EnumerateAllMethods().First(m => m.Name.EndsWith("MarkNonPersistent"));
                            ilProcessor2.Emit(OpCodes.Ldarg_0);
                            ilProcessor2.Emit(OpCodes.Ldfld, genericFieldReference);
                            ilProcessor2.Emit(OpCodes.Callvirt, ImportModle.ImportReference(methodDefinition4));
                        }
                        if (field.CustomAttributes.Any(attr => attr.AttributeType.Name.EndsWith("NonDrivable")))
                        {
                            MethodDefinition methodDefinition4 = type3.EnumerateAllMethods().First(m => m.Name.EndsWith("MarkNonDrivable"));
                            ilProcessor2.Emit(OpCodes.Ldarg_0);
                            ilProcessor2.Emit(OpCodes.Ldfld, genericFieldReference);
                            ilProcessor2.Emit(OpCodes.Callvirt, ImportModle.ImportReference(methodDefinition4));
                        }
                    }
                }
                ilProcessor2.Emit(OpCodes.Ret);
                if (!typeDefinition2.IsAbstract)
                {
                    List<WorkerFieldData> fields = new List<WorkerFieldData>();
                    GatherFieldsInHierarchy(typeDefinition2, typeDefinition2, fields);
                    MethodDefinition methodDefinition4 = typeDefinition2.Methods.First(m => m.Name.EndsWith("GetSyncMember") && m.IsVirtual);
                    methodDefinition4.Body.InitLocals = true;
                    ILProcessor ilProcessor3 = methodDefinition4.Body.GetILProcessor();
                    List<Instruction> instructionList1 = new List<Instruction>();
                    List<Instruction> instructionList2 = new List<Instruction>();
                    foreach (WorkerFieldData workerFieldData in fields)
                    {
                        if (ShouldProcessField(workerFieldData.field, typeDefinition2, null))
                        {
                            FieldReference field = workerFieldData.field.GetGenericFieldReference(workerFieldData.declaringType.FullName == typeDefinition2.FullName ? null : workerFieldData.declaringType);
                            if (field.FieldType.IsGenericParameter)
                                field = ImportModle.ImportReference(field, typeDefinition2);
                            else if (!ImportModle.Name.Contains("FrooxEngine"))
                                field = ImportModle.ImportReference(field, ImportModle.ImportReference(workerFieldData.declaringType));
                            Instruction instruction = ilProcessor3.Create(OpCodes.Ldarg_0);
                            instructionList1.Add(instruction);
                            instructionList2.Add(instruction);
                            instructionList2.Add(ilProcessor3.Create(OpCodes.Ldfld, field));
                            instructionList2.Add(ilProcessor3.Create(OpCodes.Ret));
                        }
                    }
                    Instruction target = ilProcessor3.Create(OpCodes.Newobj, constructor1);
                    instructionList2.Add(target);
                    instructionList2.Add(ilProcessor3.Create(OpCodes.Throw));
                    ilProcessor3.Emit(OpCodes.Ldarg_1);
                    ilProcessor3.Emit(OpCodes.Switch, instructionList1.ToArray());
                    ilProcessor3.Emit(OpCodes.Br, target);
                    foreach (Instruction instruction in instructionList2)
                        ilProcessor3.Append(instruction);
                    if (typeDefinition2.InheritsFrom("FrooxEngine.LogiX.LogixNode"))
                    {
                        bool flag2 = true;
                        foreach (WorkerFieldData workerFieldData in fields)
                        {
                            if (ShouldProcessField(workerFieldData.field, typeDefinition2, null))
                            {
                                TypeDefinition type3 = workerFieldData.field.FieldType.Resolve();
                                if (type3.InheritsFrom("FrooxEngine.SyncVar") || type3.InheritsFrom("FrooxEngine.SyncObject") || (type3.HasInterface("FrooxEngine.ISyncList") || type3.HasInterface("FrooxEngine.ISyncDictionary")))
                                {
                                    flag2 = false;
                                    break;
                                }
                            }
                        }
                        if (flag2)
                        {
                            MethodDefinition methodDefinition5 = new MethodDefinition("NotifyOutputsOfChange", Mono.Cecil.MethodAttributes.Family | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.HideBySig, returnType2);
                            methodDefinition5.Body.InitLocals = true;
                            typeDefinition2.Methods.Add(methodDefinition5);
                            ILProcessor ilProcessor4 = methodDefinition5.Body.GetILProcessor();
                            if (typeDefinition2.HasInterface("FrooxEngine.LogiX.IOutputElement"))
                            {
                                MethodDefinition methodDefinition6 = type1.EnumerateAllMethods().First(m => m.Name.EndsWith("NotifyChange"));
                                ilProcessor4.Emit(OpCodes.Ldarg_0);
                                ilProcessor4.Emit(OpCodes.Callvirt, ImportModle.ImportReference(methodDefinition6));
                            }
                            foreach (WorkerFieldData workerFieldData in fields)
                            {
                                if (ShouldProcessField(workerFieldData.field, typeDefinition2, null) && workerFieldData.field.FieldType.Resolve().HasInterface("FrooxEngine.LogiX.IOutputElement"))
                                {
                                    FieldReference field = workerFieldData.field.GetGenericFieldReference(workerFieldData.declaringType.FullName == typeDefinition2.FullName ? null : workerFieldData.declaringType);
                                    if (!ImportModle.Name.Contains("FrooxEngine"))
                                        field = ImportModle.ImportReference(field, ImportModle.ImportReference(workerFieldData.declaringType));
                                    workerFieldData.field.FieldType.Resolve();
                                    MethodDefinition methodDefinition6 = type1.EnumerateAllMethods().First(m => m.Name.EndsWith("NotifyChange"));
                                    ilProcessor4.Emit(OpCodes.Ldarg_0);
                                    ilProcessor4.Emit(OpCodes.Ldfld, field);
                                    ilProcessor4.Emit(OpCodes.Callvirt, ImportModle.ImportReference(methodDefinition6));
                                }
                            }
                            ilProcessor4.Emit(OpCodes.Ret);
                        }
                    }
                }
            }
            if (ilProcessor1 != null)
            {
                foreach (TypeReference typeReference in typeReferenceSet)
                {
                    Console.WriteLine("AOT Type: " + typeReference.FullName);
                    GenericInstanceType genericInstanceType = self1.MakeGenericInstanceType(typeReference);
                    MethodReference method1 = self2.MakeHostInstanceGeneric(genericInstanceType);
                    ilProcessor1.Emit(OpCodes.Call, method1);
                    ilProcessor1.Emit(OpCodes.Call, new GenericInstanceMethod(methodDefinition2)
                    {
                        GenericArguments = {
              typeReference
            }
                    });
                    if (typeReference.IsValueType)
                    {
                        MethodReference method2 = self4.MakeHostInstanceGeneric(self3.MakeGenericInstanceType(typeReference));
                        ilProcessor1.Emit(OpCodes.Call, method2);
                    }
                    foreach (TypeDefinition typeDefinition2 in typeDefinitionList)
                    {
                        if (!IsNeosEnumConstraint(typeDefinition2) || typeReference.Resolve().IsEnum)
                        {
                            MethodReference method2 = typeDefinition2.GetConstructors().FirstOrDefault().MakeHostInstanceGeneric(typeDefinition2.MakeGenericInstanceType(typeReference));
                            ilProcessor1.Emit(OpCodes.Newobj, method2);
                            ilProcessor1.Emit(OpCodes.Pop);
                        }
                    }
                }
                ilProcessor1.Emit(OpCodes.Ret);
            }
            assemblyDefinition.Write(new WriterParameters()
            {
                WriteSymbols = flag1,
                SymbolWriterProvider = flag1 ? new PdbWriterProvider() : null
            });
            assemblyDefinition.Dispose();
            foreach (MemberReference memberReference in warningWorkers)
                Console.WriteLine("Non-readonly sync members on: " + memberReference.FullName);
            return true;
        }

        private static bool IsNeosEnumConstraint(TypeDefinition type) => type.GenericParameters[0].Constraints.Count == 1 && type.GenericParameters[0].Constraints[0].ConstraintType.Name.Contains("IConvertible");

        private static TypeReference ResolveGenericParameters(
          TypeReference argument,
          TypeDefinition worker,
          GenericInstanceType genericInstance)
        {
            if (argument.IsGenericParameter)
            {
                for (int index = 0; index < worker.GenericParameters.Count; ++index)
                {
                    if (worker.GenericParameters[index].FullName == argument.FullName)
                        return genericInstance.GenericArguments[index];
                }
            }
            if (!(argument is GenericInstanceType genericInstanceType))
                return argument;
            TypeReference[] typeReferenceArray = new TypeReference[genericInstanceType.GenericArguments.Count];
            for (int index = 0; index < genericInstance.GenericArguments.Count; ++index)
            {
                TypeReference typeReference = ResolveGenericParameters(genericInstance.GenericArguments[index], worker, genericInstance);
                typeReferenceArray[index] = typeReference;
            }
            return argument.Resolve().MakeGenericInstanceType(typeReferenceArray);
        }

        private static void GatherFieldsInHierarchy(
          TypeDefinition worker,
          TypeReference instance,
          List<WorkerFieldData> fields)
        {
            if (worker.BaseType.FullName != "FrooxEngine.Worker")
            {
                TypeDefinition typeDefinition = worker.BaseType.Resolve();
                TypeReference instance1 = worker.BaseType;
                if (instance is GenericInstanceType genericInstance && instance1 is GenericInstanceType genericInstanceType)
                {
                    TypeReference[] typeReferenceArray = new TypeReference[genericInstanceType.GenericArguments.Count];
                    for (int index = 0; index < genericInstanceType.GenericArguments.Count; ++index)
                    {
                        TypeReference typeReference = ResolveGenericParameters(genericInstanceType.GenericArguments[index], worker, genericInstance);
                        typeReferenceArray[index] = typeReference;
                    }
                    instance1 = typeDefinition.MakeGenericInstanceType(typeReferenceArray);
                }
                GatherFieldsInHierarchy(typeDefinition, instance1, fields);
            }
            foreach (FieldDefinition field in worker.Fields)
                fields.Add(new WorkerFieldData(field, instance));
        }

        private static bool ShouldProcessField(
          FieldDefinition field,
          TypeDefinition type,
          HashSet<TypeDefinition> warningWorkers)
        {
            if (field.Name.Contains("__BackingField"))
                return false;
            if (field.FieldType.IsGenericParameter)
            {
                GenericParameter fieldType = (GenericParameter)field.FieldType;
                if (!fieldType.HasConstraints)
                    return false;
                bool flag = false;
                foreach (GenericParameterConstraint constraint in fieldType.Constraints)
                {
                    if (constraint.ConstraintType.Resolve().EnumerateAllInterfaces().Any(t => t.FullName == "FrooxEngine.ISyncMember"))
                    {
                        flag = true;
                        break;
                    }
                }
                if (!flag)
                    return false;
            }
            else
            {
                TypeDefinition type1 = field.FieldType.Resolve();
                if (type1 == null || type1.IsInterface || type1.IsAbstract || !type1.EnumerateAllInterfaces().Any(t => t.FullName == "FrooxEngine.ISyncMember"))
                    return false;
            }
            if (field.IsInitOnly)
                return true;
            warningWorkers?.Add(type);
            return false;
        }

        private static void DevirtualizeSyncMembers(TypeDefinition worker)
        {
            foreach (MethodDefinition method in worker.Methods)
            {
                if (method.Body != null)
                {
                    foreach (Instruction instruction in method.Body.GetILProcessor().Body.Instructions)
                    {
                        if (!(instruction.OpCode != OpCodes.Callvirt))
                        {
                            if (instruction.Operand is MethodReference callMethod && instruction.Previous?.Operand is FieldReference operand)
                            {
                                TypeDefinition typeDefinition = operand.FieldType.Resolve();
                                if (typeDefinition != null && typeDefinition.IsSealed)
                                {
                                    if (callMethod.DeclaringType.Name.Contains("SyncRefBase"))
                                    {
                                        if (callMethod.Name != "get_Target")
                                            continue;
                                    }
                                    else if (!callMethod.DeclaringType.Name.Contains("SyncField") || callMethod.Name != "get_Value")
                                        continue;
                                    MethodDefinition methodDefinition = typeDefinition.Methods.FirstOrDefault(m => m.Name == callMethod.Name);
                                    if (methodDefinition == null)
                                        Console.WriteLine(string.Format("Cannot devirtualize {0} on {1} ({2} on {3}, in method {4}), because it doesn't provide override method", callMethod, typeDefinition, operand.Name, worker.Name, method.Name));
                                    else if (!(methodDefinition.ReturnType.FullName != callMethod.ReturnType.FullName))
                                    {
                                        callMethod.DeclaringType = operand.FieldType;
                                        instruction.OpCode = OpCodes.Call;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void CollectWorkerTypes(TypeDefinition type, List<TypeDefinition> list)
        {
            if (type.InheritsFrom("FrooxEngine.Worker"))
                list.Add(type);
            foreach (TypeDefinition nestedType in type.NestedTypes)
                CollectWorkerTypes(nestedType, list);
        }

        private static MethodReference GetConstructor(
          TypeReference type,
          ModuleDefinition module,
          out bool isMethodCall)
        {
            try
            {
                if (type.IsGenericParameter)
                {
                    MethodReference methodReference = module.ImportReference(typeof(Activator)).Resolve().Methods.First(m => m.Name == "CreateInstance" && m.HasGenericParameters && !m.HasParameters).MakeGeneric(type);
                    isMethodCall = true;
                    return methodReference;
                }
                if (type.IsGenericInstance)
                {
                    GenericInstanceType genericInstanceType = (GenericInstanceType)type;
                    MethodReference method = genericInstanceType.Resolve().GetConstructors().First(ctr => !ctr.HasParameters && !ctr.IsStatic).MakeHostInstanceGeneric(genericInstanceType);
                    MethodReference methodReference = module.ImportReference(method);
                    isMethodCall = false;
                    return methodReference;
                }
                MethodDefinition methodDefinition = type.Resolve().GetConstructors().First(ctr => !ctr.HasParameters && !ctr.IsStatic);
                MethodReference methodReference1 = module.ImportReference(methodDefinition);
                isMethodCall = false;
                return methodReference1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception getting constructor for type: " + type.FullName);
                throw ex;
            }
        }

        private class WorkerFieldData
        {
            public readonly FieldDefinition field;
            public readonly TypeReference declaringType;

            public WorkerFieldData(FieldDefinition field, TypeReference declaringType)
            {
                this.field = field;
                this.declaringType = declaringType;
            }
        }
    }
}
