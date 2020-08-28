﻿using Entia.Core;
using Entia.Initializers;
using Entia.Instantiators;
using Entia.Modules;
using Entia.Modules.Template;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace Entia.Templaters
{
    public interface ITemplater
    {
        Result<(IInstantiator instantiator, IInitializer initializer)> Template(in Context context, World world);
    }

    // [System.AttributeUsage(ModuleUtility.AttributeUsage, Inherited = true, AllowMultiple = false)]
    // public sealed class TemplaterAttribute : PreserveAttribute { }

    // public sealed class Default : ITemplater
    // {
    //     public Result<(IInstantiator instantiator, IInitializer initializer)> Template(in Context context, World world)
    //     {
    //         var staticType = ReflectionUtility.GetData(context.Type);
    //         if (staticType.IsPlain || ReflectionUtility.IsPrimitive(context.Value)) return (new Constant(context.Value), new Identity());

    //         var dynamicType = ReflectionUtility.GetData(context.Value.GetType());
    //         var templaters = world.Templaters();
    //         switch (context.Value)
    //         {
    //             case System.Array array:
    //                 var elementType = dynamicType.Element;
    //                 if (elementType.IsPlain) return (new Clone(array), new Identity());

    //                 var items = new List<(int index, int reference)>(array.Length);
    //                 for (var i = 0; i < array.Length; i++)
    //                 {
    //                     var value = array.GetValue(i);
    //                     if (ReflectionUtility.IsPrimitive(value)) continue;

    //                     var result = templaters.Template(new Context(value, elementType.Type, context));
    //                     if (result.IsFailure()) return result.Fail();
    //                     if (result.TryValue(out var reference)) items.Add((i, reference.Index));
    //                 }

    //                 return (new Clone(array), new Initializers.Array(items.ToArray()));
    //             default:
    //                 var instantiator = staticType.Type.IsValueType ?
    //                     new Constant(context.Value) as IInstantiator :
    //                     new Clone(context.Value);
    //                 if (dynamicType.IsPlain) return (instantiator, new Identity());

    //                 var fields = new List<(FieldInfo field, int reference)>(dynamicType.InstanceFields.Length);
    //                 for (int i = 0; i < dynamicType.InstanceFields.Length; i++)
    //                 {
    //                     var field = dynamicType.InstanceFields[i];
    //                     var fieldType = ReflectionUtility.GetData(field.FieldType);
    //                     if (fieldType.IsPlain) continue;

    //                     var value = field.GetValue(context.Value);
    //                     if (ReflectionUtility.IsPrimitive(value)) continue;

    //                     var result = templaters.Template(new Context(value, fieldType.Type, context));
    //                     if (result.IsFailure()) return result.Fail();
    //                     if (result.TryValue(out var reference)) fields.Add((field, reference.Index));
    //                 }

    //                 return (instantiator, new Initializers.Object(fields.ToArray()));
    //         }
    //     }
    // }
}
