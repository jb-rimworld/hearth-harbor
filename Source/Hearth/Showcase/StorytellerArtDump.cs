// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using Hearth.Testing;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth
{
    /// <summary>
    /// Art reference (explicit only): writes every storyteller's large and tiny portrait as PNG, read back from the GPU,
    /// so new storyteller art can be matched to the vanilla style. Output: /private/tmp/claude-501/hh_tests/storyteller_art/.
    /// </summary>
    public sealed class StorytellerArtDumpSuite : HearthTestSuite
    {
        public override string Name => "gallery.storytellerart";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 600;

        static bool Dump(Texture2D tex, string path, TestContext t)
        {
            if (tex == null) { t.Note("no texture for " + path); return false; }
            try
            {
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0); copy.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(path, copy.EncodeToPNG());
                return true;
            }
            catch (Exception e) { t.Note("dump failed " + path + ": " + e.Message); return false; }
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            string dir = "/private/tmp/claude-501/hh_tests/storyteller_art";
            Directory.CreateDirectory(dir);
            int n = 0;
            foreach (var st in DefDatabase<StorytellerDef>.AllDefs)
            {
                if (Dump(st.portraitLargeTex, Path.Combine(dir, st.defName + "_large.png"), t)) n++;
                if (Dump(st.portraitTinyTex, Path.Combine(dir, st.defName + "_tiny.png"), t)) n++;
            }
            t.Check(n > 4, "dumped " + n + " portraits");
            yield return 1;
        }
    }
}
#endif
