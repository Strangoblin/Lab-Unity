using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Mine.ExteriorMapping
{
    /// <summary>批处理编译检查入口，显式编译 ExteriorMapping 的唯一 Pass。</summary>
    /// <remarks>批处理：-batchmode -nographics -executeMethod Mine.ExteriorMapping.ExteriorMappingValidator.Validate</remarks>
    public static class ExteriorMappingValidator
    {
        private const string ShaderPath =
            "Assets/Mine/Shaders/Render/InteriorMapping/ExteriorMapping.shader";
        private const string MaterialPath =
            "Assets/Mine/Shaders/Render/InteriorMapping/Mine_Render_ExteriorMapping_Cube.mat";
        private const string ReportPath = "/tmp/exterior-mapping-validation.txt";

        private static readonly string[] FaceProperties =
        {
            "_FaceFront",
            "_FaceBack",
            "_FaceLeft",
            "_FaceRight",
            "_FaceTop",
            "_FaceBottom"
        };

        // ════════════════════════════════════════════════════════════
        //  Validate — 加载资源、编译 Pass 0、记录消息并退出
        // ════════════════════════════════════════════════════════════
        public static void Validate()
        {
            List<string> lines = new List<string>();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            lines.Add($"shaderFound={shader != null}");
            lines.Add($"materialFound={material != null}");

            bool ok = shader != null && material != null;
            if (!ok)
            {
                WriteReport(lines);
                EditorApplication.Exit(1);
                return;
            }

            lines.Add($"shaderSupported={shader.isSupported}");
            lines.Add($"passCount={shader.passCount}");
            lines.Add($"materialShaderMatches={material.shader == shader}");
            ok &= shader.isSupported && material.shader == shader;

            foreach (string property in FaceProperties)
            {
                bool hasTexture = material.GetTexture(property) != null;
                lines.Add($"{property}Bound={hasTexture}");
                ok &= hasTexture;
            }

            bool compileAttempted = shader.passCount > 0;
            lines.Add($"compilePass0Attempted={compileAttempted}");
            ok &= compileAttempted;
            if (compileAttempted)
            {
                ShaderUtil.CompilePass(material, 0, true);
            }

            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            lines.Add($"messageCount={messages.Length}");
            foreach (ShaderMessage message in messages)
            {
                lines.Add(
                    $"message: severity={message.severity} line={message.line} " +
                    $"platform={message.platform} text={message.message}");
            }
            ok &= messages.Length == 0;

            lines.Add($"result={(ok ? "PASS" : "FAIL")}");
            WriteReport(lines);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        // ════════════════════════════════════════════════════════════
        //  WriteReport — 同时写入日志与报告文件
        // ════════════════════════════════════════════════════════════
        private static void WriteReport(List<string> lines)
        {
            string text = string.Join("\n", lines);
            File.WriteAllText(ReportPath, text + "\n");
            Debug.Log($"[ExteriorMappingValidator]\n{text}");
        }
    }
}
