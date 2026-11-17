// 品牌贴图的导入设置：不压缩（标志边缘不能有压缩噪点）、带多级渐远 + 各向异性（远看不闪）、不重复
PlayerPrefs.DeleteKey("monster.platePreview"); PlayerPrefs.Save();
foreach (var n in new[] { "plate_zh", "plate_en" })
{
    string path = "Assets/_Monster/Resources/Brand/" + n + ".png";
    var ti = (TextureImporter)AssetImporter.GetAtPath(path);
    if (ti == null) { result.Log("没导入 " + path); continue; }
    ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.streamingMipmaps = false;
    ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8; ti.npotScale = TextureImporterNPOTScale.None;
    ti.textureCompression = TextureImporterCompression.Uncompressed; ti.maxTextureSize = 2048;
    var ps = ti.GetPlatformTextureSettings("Android"); ps.overridden = true; ps.format = TextureImporterFormat.RGBA32; ps.maxTextureSize = 2048; ti.SetPlatformTextureSettings(ps);
    ti.SaveAndReimport();
    var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    result.Log(n + " " + t.width + "x" + t.height + " " + t.format + " mips=" + t.mipmapCount);
}
Monster.EditorTools.MonsterBuild.ApplySplash();
result.Log("启动画面：show=" + PlayerSettings.SplashScreen.show + " Unity标志=" + PlayerSettings.SplashScreen.showUnityLogo + " 自定义标志 " + PlayerSettings.SplashScreen.logos.Length + " 张" + (PlayerSettings.SplashScreen.logos.Length > 0 && PlayerSettings.SplashScreen.logos[0].logo ? "：" + PlayerSettings.SplashScreen.logos[0].logo.name + " " + PlayerSettings.SplashScreen.logos[0].logo.rect.size : "") + " 背景 #" + ColorUtility.ToHtmlStringRGB(PlayerSettings.SplashScreen.backgroundColor) + " 动画 " + PlayerSettings.SplashScreen.animationMode + " 排列 " + PlayerSettings.SplashScreen.drawMode);
