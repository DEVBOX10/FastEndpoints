﻿using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace FastEndpoints;

//TODO: optimize this abomination!!!

static class ComplexFormBinder
{
    internal static void Bind(PropCache fromFormProp, object requestDto, IFormCollection forms)
    {
        var propInstance = fromFormProp.PropType.CachedObjectFactory()();

        BindPropertiesRecursively(propInstance, forms, string.Empty);

        fromFormProp.PropSetter(requestDto, propInstance);

        static void BindPropertiesRecursively(object obj, IFormCollection form, string prefix)
        {
            var tObject = obj.GetType();
            var properties = tObject.CachedBindableProps();

            foreach (var prop in properties)
            {
                var propName = prop.GetCustomAttribute<BindFromAttribute>()?.Name ?? prop.Name;
                var key = string.IsNullOrEmpty(prefix)
                              ? propName
                              : $"{prefix}.{propName}";

                if (Types.IFormFile.IsAssignableFrom(prop.PropertyType))
                {
                    if (form.Files.GetFile(key) is { } file)
                        tObject.CachedSetterForProp(prop)(obj, file);
                }
                else if (Types.IEnumerableOfIFormFile.IsAssignableFrom(prop.PropertyType))
                {
                    var files = form.Files.GetFiles(key);

                    if (files.Count == 0)
                        continue;

                    var collection = new FormFileCollection();
                    collection.AddRange(files);
                    tObject.CachedSetterForProp(prop)(obj, collection);
                }
                else if (prop.PropertyType.IsClass && prop.PropertyType != Types.String && !Types.IEnumerable.IsAssignableFrom(prop.PropertyType))
                {
                    var nestedObject = prop.PropertyType.CachedObjectFactory()();

                    BindPropertiesRecursively(nestedObject, form, key);
                    tObject.CachedSetterForProp(prop)(obj, nestedObject);
                }
                else if (Types.IEnumerable.IsAssignableFrom(prop.PropertyType) && prop.PropertyType != Types.String)
                {
                    var tElement = prop.PropertyType.IsGenericType
                                       ? prop.PropertyType.GetGenericArguments()[0]
                                       : prop.PropertyType.GetElementType();

                    if (tElement is null)
                        continue;

                    var list = (IList)Types.ListOf1.MakeGenericType(tElement).CachedObjectFactory()();

                    var index = 0;

                    while (true)
                    {
                        var indexedKey = $"{key}[{index}]";
                        var item = tElement.CachedObjectFactory()();

                        BindPropertiesRecursively(item, form, indexedKey);

                        if (HasAnyPropertySet(item))
                        {
                            list.Add(item);
                            index++;
                        }
                        else
                            break;

                        static bool HasAnyPropertySet(object obj)
                        {
                            return obj.GetType().CachedBindableProps().Any(
                                p =>
                                {
                                    var val = p.GetValue(obj);

                                    if (val is IEnumerable enm)
                                        return enm.Cast<object>().Any();

                                    return val is not null && HasAnyPropertySet(val);
                                });
                        }
                    }

                    tObject.CachedSetterForProp(prop)(obj, list);
                }
                else
                {
                    if (!form.TryGetValue(key, out var val))
                        continue;

                    var res = prop.PropertyType.CachedValueParser()(val);
                    if (res.IsSuccess)
                        tObject.CachedSetterForProp(prop)(obj, res.Value);
                }
            }
        }
    }
}