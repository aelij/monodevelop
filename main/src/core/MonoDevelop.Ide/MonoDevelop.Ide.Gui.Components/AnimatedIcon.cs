//
// AnimatedIcon.cs
//
// Author:
//       Lluis Sanchez <lluis@xamarin.com>
//
// Copyright (c) 2012 Xamarin Inc
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using Gtk;
using Xwt.Drawing;
using Image = Xwt.Drawing.Image;

namespace MonoDevelop.Ide.Gui.Components
{
    /// <summary>
    /// An animation spec is a sequence of animation frames. Frames are separated using semicolons.
    /// Each frame can be an image (using a regular image spec), an effect, or a pause.
    /// <example>
    /// <c>res:build1.png;morph;res:build2.png;morph</c>
    /// </example>
    /// Supported effects are: <c>fade-out</c>, <c>fade-in</c>, <c>morph</c>
    /// </summary>
    public class AnimatedIcon
    {
        private readonly IconSize size;

        private const int DefaultPause = 200;
        private List<Image> images;
        private List<int> pauses;

        private static readonly Dictionary<string, Type> AnimationItems = new Dictionary<string, Type>();

        static AnimatedIcon()
        {
            AnimationItems["morph"] = typeof(MorphEffect);
            AnimationItems["fade-in"] = typeof(FadeInEffect);
            AnimationItems["fade-out"] = typeof(FadeOutEffect);
        }

        public AnimatedIcon(string animationSpec, IconSize size)
        {
            this.size = size;
            AnimationSpec = animationSpec;
            Parse(animationSpec);
        }

        private void Parse(string animationSpec)
        {
            List<AnimationItem> parsedItems = new List<AnimationItem>();
            string[] items = animationSpec.Split(';');
            AnimationItem last = null;

            foreach (var item in items)
            {
                int i = item.IndexOf(':');
                var tname = i != -1 ? item.Substring(0, i) : item;
                int pause;
                Type type;
                AnimationItem aitem;

                if (AnimationItems.TryGetValue(tname, out type))
                {
                    aitem = (AnimationItem)Activator.CreateInstance(type);
                    aitem.Parse(item);
                }
                else if (int.TryParse(item, out pause))
                {
                    aitem = new PauseItem { Pause = pause };
                }
                else
                {
                    // It must be an image
                    var id = ImageService.GetStockId(item, size);
                    var img = ImageService.GetIcon(id);
                    if (img == null)
                        continue;
                    aitem = new ImageItem { Image = img };
                }
                if (last != null)
                    last.NextItem = aitem;
                aitem.PreviousItem = last;
                parsedItems.Add(aitem);
                last = aitem;
            }

            if (parsedItems.Count > 0)
            {
                // Close the chain
                parsedItems[0].PreviousItem = parsedItems[parsedItems.Count - 1];
                parsedItems[parsedItems.Count - 1].NextItem = parsedItems[0];
            }

            images = new List<Image>();
            pauses = new List<int>();
            bool lastWasImage = false;

            foreach (var aitem in parsedItems)
            {
                foreach (var frame in aitem.GetFrames())
                {
                    var item = frame as Image;
                    if (item != null)
                    {
                        if (lastWasImage)
                        {
                            pauses.Add(DefaultPause);
                        }
                        images.Add(item);
                        lastWasImage = true;
                    }
                    else
                    {
                        if (!lastWasImage)
                        {
                            if (pauses.Count > 0)
                                pauses[pauses.Count - 1] = pauses[pauses.Count - 1] + (int)frame;
                            else
                            {
                                // Pause before any image. Add a dummy image
                                images.Add(ImageService.GetIcon("md-empty"));
                                pauses.Add((int)frame);
                            }
                        }
                        else
                            pauses.Add((int)frame);
                        lastWasImage = false;
                    }
                }
            }
            if (pauses.Count < images.Count)
                pauses.Add(DefaultPause);
        }

        public Image FirstFrame => images.Count > 0 ? images[0] : ImageService.GetIcon("md-empty");

        public string AnimationSpec { get; }

        public IDisposable StartAnimation(Action<Image> renderer)
        {
            int currentFrame = 0;
            return DispatchService.RunAnimation(delegate
            {
                renderer(images[currentFrame]);
                var res = pauses[currentFrame];
                currentFrame = (currentFrame + 1) % images.Count;
                return res;
            });
        }

        private abstract class AnimationItem
        {
            private List<object> frames;

            internal AnimationItem NextItem;
            internal AnimationItem PreviousItem;
            private bool renderingFrames;

            public virtual void Parse(string spec)
            {
            }

            internal List<object> GetFrames()
            {
                RenderFrames();
                return frames;
            }

            public Image PreviousFrame
            {
                get
                {
                    PreviousItem.RenderFrames();
                    var last = (Image)PreviousItem.frames.LastOrDefault(f => f is Image);
                    if (last != null)
                        return last;
                    return PreviousItem.PreviousFrame;
                }
            }

            public Image NextFrame
            {
                get
                {
                    NextItem.RenderFrames();
                    var first = (Image)NextItem.frames.FirstOrDefault(f => f is Image);
                    if (first != null)
                        return first;
                    return NextItem.NextFrame;
                }
            }

            private void RenderFrames()
            {
                if (renderingFrames)
                    throw new Exception("Invalid animation sequence");
                if (frames == null)
                {
                    renderingFrames = true;
                    frames = new List<object>();
                    OnRenderFrames();
                    renderingFrames = false;
                }
            }

            public abstract void OnRenderFrames();

            protected void AddImage(Image image)
            {
                frames.Add(image);
            }

            protected void AddPause(int ms)
            {
                frames.Add(ms);
            }
        }

        private class ImageItem : AnimationItem
        {
            public Image Image { get; set; }

            public override void OnRenderFrames()
            {
                AddImage(Image);
            }
        }

        private class PauseItem : AnimationItem
        {
            public int Pause { get; set; }

            public override void OnRenderFrames()
            {
                AddPause(Pause);
            }
        }

        private class FadeOutEffect : AnimationItem
        {
            public override void OnRenderFrames()
            {
                var icon = PreviousFrame;
                for (int n = 0; n < 10; n++)
                {
                    AddImage(icon.WithAlpha((9 - n) / 10.0));
                    AddPause(60);
                }
            }
        }

        private class FadeInEffect : AnimationItem
        {
            public override void OnRenderFrames()
            {
                var icon = NextFrame;
                for (int n = 0; n < 10; n++)
                {
                    AddImage(icon.WithAlpha(n / 10.0));
                    AddPause(60);
                }
            }
        }

        private class MorphEffect : AnimationItem
        {
            public override void OnRenderFrames()
            {
                var prev = PreviousFrame;
                var next = NextFrame;
                for (int n = 0; n < 10; n++)
                {
                    var img1 = next.WithAlpha(n / 10.0);
                    var img2 = prev.WithAlpha((9 - n) / 10.0);
                    var ib = new ImageBuilder(img1.Size.Width, img2.Size.Height);
                    ib.Context.DrawImage(img1, 0, 0, n / 10.0);
                    ib.Context.DrawImage(img2, 0, 0, (9 - n) / 10.0);
                    AddImage(ib.ToVectorImage());
                    AddPause(60);
                }
            }
        }
    }
}
