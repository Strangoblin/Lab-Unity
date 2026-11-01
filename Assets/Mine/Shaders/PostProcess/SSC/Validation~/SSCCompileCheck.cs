using System;
using UnityEditor;
using UnityEngine;
public class Script
{
    public static object Main()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Mine/Shaders/PostProcess/SSC/SSC.shader");
        if (shader == null) throw new Exception("SSC.shader missing");
        var results = new System.Collections.Generic.List<string>();
        ShaderUtil.ClearShaderMessages(shader);
        foreach (string keyword in new[]{"SSC_RAY_COUNT_32","SSC_RAY_COUNT_64","SSC_RAY_COUNT_128","SSC_RAY_COUNT_256"})
        {
            var material = new Material(shader);
            material.EnableKeyword(keyword);
            for (int pass = 0; pass < shader.passCount; pass++)
                ShaderUtil.CompilePass(material, pass, true);
            UnityEngine.Object.DestroyImmediate(material);
        }
        var messages = ShaderUtil.GetShaderMessages(shader);
        results.Add("passes=" + shader.passCount + " messages=" + messages.Length);
        foreach (var message in messages)
            results.Add(message.severity + ": " + message.message);
        return results;
    }
}
