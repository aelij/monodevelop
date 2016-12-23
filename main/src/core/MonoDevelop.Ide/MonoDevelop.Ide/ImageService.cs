// ImageService.cs
//
// Author:
//   Mike Krüger <mkrueger@novell.com>
//
// Copyright (C) 2009 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using Gdk;
using Gtk;
using MonoDevelop.Components;
using MonoDevelop.Core;
using MonoDevelop.Ide.Gui.Components;
using Xwt;
using Xwt.Drawing;
using Color = Xwt.Drawing.Color;
using IconSize = Gtk.IconSize;
using Image = Xwt.Drawing.Image;
using TreeStore = Gtk.TreeStore;

namespace MonoDevelop.Ide
{
    public static class ImageService
    {
        private const string StockIconsXmlFileName = ".StockIcons.xml";
        private static readonly IconFactory IconFactory = new IconFactory();

        // Map of all animations
        private static readonly Dictionary<string, AnimatedIcon> AnimationFactory = new Dictionary<string, AnimatedIcon>();

        private static readonly Dictionary<string, string> ComposedIcons = new Dictionary<string, string>();

        // Dictionary of extension nodes by stock icon id. It holds nodes that have not yet been loaded
        private static readonly Dictionary<string, List<StockIcon>> IconStock = new Dictionary<string, List<StockIcon>>();

        private static readonly Requisition[] IconSizes = new Requisition[7];

        private static readonly Dictionary<string, Image> Icons = new Dictionary<string, Image>();

        private static readonly Dictionary<ResourceInfo, CustomImageLoader> ImageLoaders = new Dictionary<ResourceInfo, CustomImageLoader>();

        static ImageService()
        {
            IconFactory.AddDefault();
            IconId.IconNameRequestHandler = EnsureStockIconIsLoaded;

            for (int i = 0; i < IconSizes.Length; i++)
            {
                int w, h;
                if (!Icon.SizeLookup((IconSize)i, out w, out h))
                    w = h = -1;
                IconSizes[i].Width = w;
                IconSizes[i].Height = h;
            }
            if (Platform.IsWindows)
            {
                IconSizes[(int)IconSize.Menu].Width = 16;
                IconSizes[(int)IconSize.Menu].Height = 16;
            }
        }

        private static Image LoadStockIcon(StockIcon icon)
        {
            return LoadStockIcon(icon.ResourceInfo, icon.stockid, icon.resource, icon.size, icon.animation);
        }

        private static Image LoadStockIcon(ResourceInfo ri, string stockId, string resource, IconSize iconSize, string animation)
        {
            try
            {
                AnimatedIcon animatedIcon = null;

                Image img = null;

                if (!string.IsNullOrEmpty(resource))
                {
                    CustomImageLoader loader;
                    if (!ImageLoaders.TryGetValue(ri, out loader))
                    {
                        loader = ImageLoaders[ri] = new CustomImageLoader(ri);
                    }
                    img = Image.FromCustomLoader(loader, resource);
                }
                else if (!string.IsNullOrEmpty(animation))
                {
                    string id = GetStockIdForImageSpec(ri, "animation:" + animation, iconSize);
                    img = GetIcon(id, iconSize);
                    // This *should* be an animation
                    AnimationFactory.TryGetValue(id, out animatedIcon);
                }

                if (animatedIcon != null)
                    AddToAnimatedIconFactory(stockId, animatedIcon);

                return img;

            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error loading icon '{stockId}'", ex);
                return null;
            }
        }

        public static void Initialize()
        {
            //forces static constructor to run
        }

        public static Image GetIcon(string name, IconSize size = IconSize.Menu)
        {
            // Converts an image spec into a real stock icon id
            name = GetStockIdForImageSpec(name, size);
            return GetIcon(name).WithSize(size);
        }

        public static Image GetIcon(string name)
        {
            return GetIcon(name, true);
        }

        public static Image GetIcon(string name, bool generateDefaultIcon)
        {
            name = name ?? "";

            Image img;
            if (Icons.TryGetValue(name, out img))
                return img;

            if (string.IsNullOrEmpty(name))
            {
                LoggingService.LogWarning("Empty icon requested. Stack Trace: " + Environment.NewLine + Environment.StackTrace);
                Icons[name] = img = GetMissingIcon();
                return img;
            }

            //if an icon name begins with '#', we assume it's a hex colour
            if (name[0] == '#')
            {
                Icons[name] = img = CreateColorBlock(name).ToXwtImage();
                return img;
            }

            EnsureStockIconIsLoaded(name);

            // Try again since it may have already been registered
            if (Icons.TryGetValue(name, out img))
                return img;

            if (generateDefaultIcon)
            {
                LoggingService.LogWarning("Unknown icon: " + name);
                return GetMissingIcon();
            }

            return Icons[name] = Toolkit.CurrentEngine.WrapImage(name);
        }

        private static Image GetMissingIcon()
        {
            Image img;
            if (Icons.TryGetValue("gtk-missing-image", out img))
                return img;

            EnsureStockIconIsLoaded("gtk-missing-image");

            // Try again since it may have already been registered
            if (Icons.TryGetValue("gtk-missing-image", out img))
                return img;

            // fallback to default Gtk icon if the Gtk theme has one
            if (IconTheme.Default.HasIcon("gtk-missing-image"))
                return Icons["gtk-missing-image"] = img = GtkUtil.GtkToolkit.WrapImage("gtk-missing-image");

            // we should never end up here, log an error
            LoggingService.LogError("Loading gtk-missing-image icon failed.");
            return CreateColorIcon("#FF00FF");
        }

        internal static void EnsureStockIconIsLoaded(string stockId)
        {
            if (string.IsNullOrEmpty(stockId))
                return;

            List<StockIcon> stockIcon;
            if (IconStock.TryGetValue(stockId, out stockIcon))
            {
                var frames = new List<Image>();
                //determine whether there's a wildcarded image
                bool hasWildcard = false;
                foreach (var i in stockIcon)
                {
                    if (i.size == IconSize.Invalid)
                        hasWildcard = true;
                }
                //load all the images
                foreach (var i in stockIcon)
                {
                    var si = LoadStockIcon(i);
                    if (si != null)
                        frames.Add(si);
                }
                //if there's no wildcard, find the "biggest" version and make it a wildcard
                if (!hasWildcard)
                {
                    int biggest = 0, biggestSize = IconSizes[(int)stockIcon[0].size].Width;
                    for (int i = 1; i < stockIcon.Count; i++)
                    {
                        int w = IconSizes[(int)stockIcon[i].size].Width;
                        if (w > biggestSize)
                        {
                            biggest = i;
                            biggestSize = w;
                        }
                    }
                    //	LoggingService.LogWarning ("Stock icon '{0}' registered without wildcarded version.", stockId);
                    LoadStockIcon(stockIcon[biggest]);

                }
                // Icon loaded, it can be removed from the pending icon collection
                IconStock.Remove(stockId);

                if (frames.Count > 0)
                    Icons[stockId] = Image.CreateMultiSizeIcon(frames);
            }
        }

        public static void LoadDefaultStockSet()
        {
            LoadStockSet(typeof(ImageService).Assembly);
        }

        public static void LoadStockSet(Assembly assembly)
        {
            var iconSets = assembly.GetManifestResourceNames().Where(x => x.EndsWith(StockIconsXmlFileName));
            foreach (var iconSet in iconSets)
            {
                var ns = iconSet.Substring(0, iconSet.Length - StockIconsXmlFileName.Length);
                var icons = ReadIcons(assembly, ns);
                foreach (var stockIcon in icons)
                {
                    List<StockIcon> list;
                    if (!IconStock.TryGetValue(stockIcon.stockid, out list))
                    {
                        IconStock[stockIcon.stockid] = list = new List<StockIcon>();
                    }
                    list.Add(stockIcon);

                }
            }
        }

        private static StockIcon[] ReadIcons(Assembly assembly, string ns)
        {
            using (var stream = assembly.GetManifestResourceStream(ns + StockIconsXmlFileName))
            {
                if (stream == null) throw new InvalidOperationException("Missing resource");

                var serializer = new XmlSerializer(typeof(StockIcon[]));
                var icons = (StockIcon[])serializer.Deserialize(stream);

                var resourceInfo = new ResourceInfo(assembly, ns);
                foreach (var icon in icons)
                {
                    icon.ResourceInfo = resourceInfo;
                }

                return icons;
            }
        }

        public class StockIcon
        {
            [XmlAttribute]
            public string stockid { get; set; }
            [XmlAttribute]
            public string resource { get; set; }
            [XmlAttribute]
            public string animation { get; set; }
            [XmlAttribute]
            public IconSize size { get; set; }

            [XmlIgnore]
            internal ResourceInfo ResourceInfo { get; set; }
        }

        private static Image CreateColorIcon(string name)
        {
            var color = Color.FromName(name);
            using (var ib = new ImageBuilder(16, 16))
            {
                ib.Context.Rectangle(0, 0, 16, 16);
                ib.Context.SetColor(color);
                ib.Context.Fill();
                return ib.ToVectorImage();
            }
        }

        private static Pixbuf CreateColorBlock(string name)
        {
            int w, h;
            if (!Icon.SizeLookup(IconSize.Menu, out w, out h))
                w = h = 22;
            Pixbuf p = new Pixbuf(Colorspace.Rgb, true, 8, w, h);
            uint color;
            if (!TryParseColourFromHex(name, false, out color))
                //if lookup fails, make it transparent
                color = 0xffffff00u;
            p.Fill(color);
            return p;
        }

        private static bool TryParseColourFromHex(string str, bool alpha, out uint val)
        {
            val = 0x0;
            if (str.Length != (alpha ? 9 : 7))
                return false;

            for (int stringIndex = 1; stringIndex < str.Length; stringIndex++)
            {
                uint bits;
                switch (str[stringIndex])
                {
                    case '0':
                        bits = 0;
                        break;
                    case '1':
                        bits = 1;
                        break;
                    case '2':
                        bits = 2;
                        break;
                    case '3':
                        bits = 3;
                        break;
                    case '4':
                        bits = 4;
                        break;
                    case '5':
                        bits = 5;
                        break;
                    case '6':
                        bits = 6;
                        break;
                    case '7':
                        bits = 7;
                        break;
                    case '8':
                        bits = 8;
                        break;
                    case '9':
                        bits = 9;
                        break;
                    case 'A':
                    case 'a':
                        bits = 10;
                        break;
                    case 'B':
                    case 'b':
                        bits = 11;
                        break;
                    case 'C':
                    case 'c':
                        bits = 12;
                        break;
                    case 'D':
                    case 'd':
                        bits = 13;
                        break;
                    case 'E':
                    case 'e':
                        bits = 14;
                        break;
                    case 'F':
                    case 'f':
                        bits = 15;
                        break;
                    default:
                        return false;
                }

                val = (val << 4) | bits;
            }
            if (!alpha)
                val = (val << 8) | 0xff;
            return true;
        }

        public static Gtk.Image GetImage(string name, IconSize size)
        {
            var img = new Gtk.Image();
            img.LoadIcon(name, size);
            return img;
        }

        public static string GetStockId(string filename)
        {
            return GetStockId(filename, IconSize.Invalid);
        }

        public static string GetStockId(string filename, IconSize iconSize)
        {
            return GetStockIdForImageSpec(filename, iconSize);
        }

        private static void AddToAnimatedIconFactory(string stockId, AnimatedIcon aicon)
        {
            AnimationFactory[stockId] = aicon;
        }

        private static string InternalGetStockIdFromResource(ResourceInfo ri, string id, IconSize size)
        {
            if (!id.StartsWith("res:", StringComparison.Ordinal))
                return id;

            id = id.Substring(4);
            string stockId = "__asm" + ri.Id + "__" + id + "__" + size;
            if (!Icons.ContainsKey(stockId))
            {
                Icons[stockId] = LoadStockIcon(ri, stockId, id, size, null);
            }
            return stockId;
        }

        private static string InternalGetStockIdFromAnimation(ResourceInfo ri, string id, IconSize size)
        {
            if (!id.StartsWith("animation:", StringComparison.Ordinal))
                return id;

            id = id.Substring(10);

            string stockId = "__asm" + ri.Id + "__" + id + "__" + size;
            if (!Icons.ContainsKey(stockId))
            {
                var aicon = new AnimatedIcon(id, size);
                AddToAnimatedIconFactory(stockId, aicon);
                Icons[stockId] = aicon.FirstFrame;
            }
            return stockId;
        }

        private static string GetComposedIcon(string[] ids, IconSize size)
        {
            string id = string.Join("_", ids);
            string cid;
            if (ComposedIcons.TryGetValue(id, out cid))
                return cid;
            ICollection col = size == IconSize.Invalid ? Enum.GetValues(typeof(IconSize)) : new object[] { size };
            var frames = new List<Image>();
            foreach (IconSize sz in col)
            {
                if (sz == IconSize.Invalid)
                    continue;
                ImageBuilder ib = null;
                Image icon = null;
                for (int n = 0; n < ids.Length; n++)
                {
                    var px = GetIcon(ids[n], sz);
                    if (px == null)
                    {
                        LoggingService.LogError("Error creating composed icon {0} at size {1}. Icon {2} is missing.", id, sz, ids[n]);
                        break;
                    }

                    if (n == 0)
                    {
                        ib = new ImageBuilder(px.Width, px.Height);
                        ib.Context.DrawImage(px, 0, 0);
                        icon = px;
                        continue;
                    }

                    if (icon.Width != px.Width || icon.Height != px.Height)
                        px = px.WithSize(icon.Width, icon.Height);

                    ib.Context.DrawImage(px, 0, 0);
                }
                frames.Add(ib.ToVectorImage());
            }

            Icons[id] = Image.CreateMultiSizeIcon(frames);
            ComposedIcons[id] = id;
            return id;
        }

        private static string GetStockIdForImageSpec(string filename, IconSize size)
        {
            return GetStockIdForImageSpec(null, filename, size);
        }

        private static string GetStockIdForImageSpec(ResourceInfo ri, string filename, IconSize size)
        {
            if (String.IsNullOrEmpty(filename))
                return String.Empty;
            if (filename.IndexOf('|') == -1)
                return PrivGetStockId(ri, filename, size);

            string[] parts = filename.Split('|');
            for (int n = 0; n < parts.Length; n++)
            {
                parts[n] = PrivGetStockId(ri, parts[n], size);
            }
            return GetComposedIcon(parts, size);
        }

        private static string PrivGetStockId(ResourceInfo ri, string filename, IconSize size)
        {
            if (ri != null && filename.StartsWith("res:", StringComparison.Ordinal))
                return InternalGetStockIdFromResource(ri, filename, size);

            if (filename.StartsWith("animation:", StringComparison.Ordinal))
                return InternalGetStockIdFromAnimation(ri, filename, size);

            return filename;
        }

        public static bool IsAnimation(string iconId, IconSize size)
        {
            EnsureStockIconIsLoaded(iconId);
            string id = GetStockIdForImageSpec(iconId, size);
            return AnimationFactory.ContainsKey(id);
        }

        public static AnimatedIcon GetAnimatedIcon(string iconId)
        {
            return GetAnimatedIcon(iconId, IconSize.Button);
        }

        public static AnimatedIcon GetAnimatedIcon(string iconId, IconSize size)
        {
            EnsureStockIconIsLoaded(iconId);
            string id = GetStockIdForImageSpec(iconId, size);

            AnimatedIcon aicon;
            AnimationFactory.TryGetValue(id, out aicon);
            return aicon;
        }

        private static readonly List<WeakReference> AnimatedImages = new List<WeakReference>();

        private class AnimatedImageInfo
        {
            public readonly Gtk.Image Image;
            public readonly AnimatedIcon AnimatedIcon;
            public IDisposable Animation;

            public AnimatedImageInfo(Gtk.Image img, AnimatedIcon anim)
            {
                Image = img;
                AnimatedIcon = anim;
                img.Realized += HandleRealized;
                img.Unrealized += HandleUnrealized;
                img.Destroyed += HandleDestroyed;
                if (img.IsRealized)
                    StartAnimation();
            }

            private void StartAnimation()
            {
                if (Animation == null)
                {
                    Animation = AnimatedIcon.StartAnimation(delegate (Image pix)
                    {
                        Image.Pixbuf = pix.ToPixbuf();
                    });
                }
            }

            private void StopAnimation()
            {
                if (Animation != null)
                {
                    Animation.Dispose();
                    Animation = null;
                }
            }

            private void HandleDestroyed(object sender, EventArgs e)
            {
                UnregisterImageAnimation(this);
            }

            private void HandleUnrealized(object sender, EventArgs e)
            {
                StopAnimation();
            }

            private void HandleRealized(object sender, EventArgs e)
            {
                StartAnimation();
            }

            public void Dispose()
            {
                StopAnimation();
                Image.Realized -= HandleRealized;
                Image.Unrealized -= HandleUnrealized;
                Image.Destroyed -= HandleDestroyed;
            }
        }

        public static void LoadIcon(this Gtk.Image image, string iconId, IconSize size)
        {
            AnimatedImageInfo ainfo = AnimatedImages.Select(a => (AnimatedImageInfo)a.Target).FirstOrDefault(a => a != null && a.Image == image);
            if (ainfo != null)
            {
                if (ainfo.AnimatedIcon.AnimationSpec == iconId)
                    return;
                UnregisterImageAnimation(ainfo);
            }
            if (IsAnimation(iconId, size))
            {
                var anim = GetAnimatedIcon(iconId);
                ainfo = new AnimatedImageInfo(image, anim)
                {
                    Animation = anim.StartAnimation(delegate (Image pix) { image.Pixbuf = pix.ToPixbuf(); })
                };
                AnimatedImages.Add(new WeakReference(ainfo));
            }
            else
                image.SetFromStock(iconId, size);
        }

        private static void UnregisterImageAnimation(AnimatedImageInfo ainfo)
        {
            ainfo.Dispose();
            AnimatedImages.RemoveAll(a => (AnimatedImageInfo)a.Target == ainfo);
        }

        private static readonly List<WeakReference> AnimatedTreeStoreIconImages = new List<WeakReference>();

        private class AnimatedTreeStoreIconInfo
        {
            public readonly TreeStore TreeStore;
            public readonly AnimatedIcon AnimatedIcon;
            public IDisposable Animation;
            public readonly string IconId;
            public TreeIter Iter;
            public readonly int Column;

            public AnimatedTreeStoreIconInfo(TreeStore treeStore, TreeIter iter, int column, AnimatedIcon anim, string iconId)
            {
                TreeStore = treeStore;
                Iter = iter;
                Column = column;
                AnimatedIcon = anim;
                IconId = iconId;
                TreeStore.RowDeleted += HandleRowDeleted;
                StartAnimation();
            }

            private void HandleRowDeleted(object o, RowDeletedArgs args)
            {
                TreeIter outIter;
                if (TreeStore.GetIter(out outIter, args.Path) && outIter.Equals(Iter))
                {
                    UnregisterTreeAnimation(this);
                }
            }

            private void StartAnimation()
            {
                if (Animation == null)
                {
                    Animation = AnimatedIcon.StartAnimation(delegate (Image pix)
                    {
                        if (TreeStore.IterIsValid(Iter))
                        {
                            TreeStore.SetValue(Iter, Column, pix);
                        }
                        else
                        {
                            UnregisterTreeAnimation(this);
                        }
                    });
                }
            }

            private void StopAnimation()
            {
                if (Animation != null)
                {
                    Animation.Dispose();
                    Animation = null;
                }
            }

            public void Dispose()
            {
                TreeStore.RowDeleted -= HandleRowDeleted;
                StopAnimation();
            }
        }

        public static void LoadIcon(this TreeStore treeStore, TreeIter iter, int column, string iconId, IconSize size)
        {
            var ainfo = AnimatedTreeStoreIconImages.Select(a => (AnimatedTreeStoreIconInfo)a.Target).FirstOrDefault(a => a != null && a.TreeStore == treeStore && a.Iter.Equals(iter) && a.Column == column);
            if (ainfo != null)
            {
                if (ainfo.IconId == iconId)
                    return;
                UnregisterTreeAnimation(ainfo);
            }
            if (iconId == null)
            {
                treeStore.SetValue(iter, column, CellRendererImage.NullImage);
            }
            else if (IsAnimation(iconId, size))
            {
                var anim = GetAnimatedIcon(iconId);
                ainfo = new AnimatedTreeStoreIconInfo(treeStore, iter, column, anim, iconId);
                AnimatedTreeStoreIconImages.Add(new WeakReference(ainfo));
            }
            else
            {
                treeStore.SetValue(iter, column, GetIcon(iconId));
            }
        }

        private static void UnregisterTreeAnimation(AnimatedTreeStoreIconInfo ainfo)
        {
            ainfo.Dispose();
            AnimatedTreeStoreIconImages.RemoveAll(a => (AnimatedTreeStoreIconInfo)a.Target == ainfo);
        }
    }

    internal class ResourceInfo
    {
        public ResourceInfo(Assembly assembly, string ns)
        {
            Assembly = assembly;
            Namespace = ns;
        }

        public Assembly Assembly { get; }

        public string Namespace { get; }

        public string Id => Assembly.GetName().Name + "#" + Namespace;
    }

    internal class CustomImageLoader : IImageLoader
    {
        private readonly ResourceInfo resourceInfo;

        private IEnumerable<string> resources;

        public CustomImageLoader(ResourceInfo resourceInfo)
        {
            this.resourceInfo = resourceInfo;
        }

        public IEnumerable<string> GetAlternativeFiles(string fileName, string baseName, string ext)
        {
            return resources ?? (resources = resourceInfo.Assembly.GetManifestResourceNames()
                .Where(x => x.StartsWith(resourceInfo.Namespace)).ToArray());
        }

        public Stream LoadImage(string fileName)
        {
            //var resourceName = $"{resourceInfo.Namespace}.{fileName}";
            var resourceName = fileName;
            var stream = resourceInfo.Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException("Missing resource " + resourceName);
            }
            return stream;
        }
    }
}
