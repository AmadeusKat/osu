﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays.BeatmapListing;
using osu.Game.Overlays.Direct;
using osu.Game.Rulesets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays
{
    public class BeatmapListingOverlay : FullscreenOverlay
    {
        [Resolved]
        private PreviewTrackManager previewTrackManager { get; set; }

        [Resolved]
        private RulesetStore rulesets { get; set; }

        private SearchBeatmapSetsRequest getSetsRequest;
        private CancellationTokenSource cancellationToken;

        private Container panelsPlaceholder;
        private BeatmapListingSearchSection searchSection;
        private BeatmapListingSortTabControl sortControl;

        public BeatmapListingOverlay()
            : base(OverlayColourScheme.Blue)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourProvider.Background6
                },
                new BasicScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ScrollbarVisible = false,
                    Child = new ReverseChildIDFillFlowContainer<Drawable>
                    {
                        AutoSizeAxes = Axes.Y,
                        RelativeSizeAxes = Axes.X,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 10),
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Y,
                                RelativeSizeAxes = Axes.X,
                                Direction = FillDirection.Vertical,
                                Masking = true,
                                EdgeEffect = new EdgeEffectParameters
                                {
                                    Colour = Color4.Black.Opacity(0.25f),
                                    Type = EdgeEffectType.Shadow,
                                    Radius = 3,
                                    Offset = new Vector2(0f, 1f),
                                },
                                Children = new Drawable[]
                                {
                                    new BeatmapListingHeader(),
                                    searchSection = new BeatmapListingSearchSection(),
                                }
                            },
                            new Container
                            {
                                AutoSizeAxes = Axes.Y,
                                RelativeSizeAxes = Axes.X,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = ColourProvider.Background4,
                                    },
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Children = new Drawable[]
                                        {
                                            new Container
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                Height = 40,
                                                Children = new Drawable[]
                                                {
                                                    new Box
                                                    {
                                                        RelativeSizeAxes = Axes.Both,
                                                        Colour = ColourProvider.Background5
                                                    },
                                                    sortControl = new BeatmapListingSortTabControl
                                                    {
                                                        Anchor = Anchor.CentreLeft,
                                                        Origin = Anchor.CentreLeft,
                                                        Margin = new MarginPadding { Left = 20 }
                                                    }
                                                }
                                            },
                                            panelsPlaceholder = new Container
                                            {
                                                AutoSizeAxes = Axes.Y,
                                                RelativeSizeAxes = Axes.X,
                                                Padding = new MarginPadding { Horizontal = 20 },
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            var sortCriteria = sortControl.Current;
            var sortDirection = sortControl.SortDirection;

            searchSection.Query.BindValueChanged(query =>
            {
                sortCriteria.Value = string.IsNullOrEmpty(query.NewValue) ? DirectSortCriteria.Ranked : DirectSortCriteria.Relevance;
                sortDirection.Value = SortDirection.Descending;

                Scheduler.AddOnce(updateSearch);
            });

            searchSection.Ruleset.BindValueChanged(_ => Scheduler.AddOnce(updateSearch));
            searchSection.Category.BindValueChanged(_ => Scheduler.AddOnce(updateSearch));
            sortCriteria.BindValueChanged(_ => Scheduler.AddOnce(updateSearch));
            sortDirection.BindValueChanged(_ => Scheduler.AddOnce(updateSearch));
        }

        private void updateSearch()
        {
            if (!IsLoaded)
                return;

            if (State.Value == Visibility.Hidden)
                return;

            if (API == null)
                return;

            getSetsRequest?.Cancel();
            cancellationToken?.Cancel();
            previewTrackManager.StopAnyPlaying(this);

            panelsPlaceholder.FadeColour(Color4.DimGray, 400, Easing.OutQuint);

            getSetsRequest = new SearchBeatmapSetsRequest(
                searchSection.Query.Value,
                searchSection.Ruleset.Value,
                searchSection.Category.Value,
                sortControl.Current.Value,
                sortControl.SortDirection.Value);

            getSetsRequest.Success += response => Schedule(() => recreatePanels(response));

            API.Queue(getSetsRequest);
        }

        private void recreatePanels(SearchBeatmapSetsResponse response)
        {
            if (response.Total == 0)
            {
                searchSection.BeatmapSet = null;

                LoadComponentAsync(new NotFoundDrawable(), addContentToPlaceholder, (cancellationToken = new CancellationTokenSource()).Token);
                return;
            }

            var beatmaps = response.BeatmapSets.Select(r => r.ToBeatmapSet(rulesets)).ToList();

            var newPanels = new FillFlowContainer<DirectPanel>
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Spacing = new Vector2(10),
                Alpha = 0,
                Margin = new MarginPadding { Vertical = 15 },
                ChildrenEnumerable = beatmaps.Select<BeatmapSetInfo, DirectPanel>(b => new DirectGridPanel(b)
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                })
            };

            LoadComponentAsync(newPanels, loaded =>
            {
                addContentToPlaceholder(loaded);
                loaded.FadeIn(600, Easing.OutQuint);
                searchSection.BeatmapSet = beatmaps.First();
            }, (cancellationToken = new CancellationTokenSource()).Token);
        }

        private void addContentToPlaceholder(Drawable content)
        {
            panelsPlaceholder.Clear();
            panelsPlaceholder.FadeColour(Color4.White);
            panelsPlaceholder.Add(content);
        }

        protected override void Dispose(bool isDisposing)
        {
            getSetsRequest?.Cancel();
            cancellationToken?.Cancel();

            base.Dispose(isDisposing);
        }

        private class NotFoundDrawable : CompositeDrawable
        {
            public NotFoundDrawable()
            {
                RelativeSizeAxes = Axes.X;
                Height = 250;
                Margin = new MarginPadding { Top = 15 };
            }

            [BackgroundDependencyLoader]
            private void load(TextureStore textures)
            {
                AddInternal(new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Y,
                    AutoSizeAxes = Axes.X,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(10, 0),
                    Children = new Drawable[]
                    {
                        new Sprite
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            FillMode = FillMode.Fit,
                            Texture = textures.Get(@"Online/not-found")
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = @"... nope, nothing found.",
                        }
                    }
                });
            }
        }
    }
}
