﻿//------------------------------------------------------------------------------
// <copyright file="ExplosionViewportAdornment.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using PowerMode.Extensions;

namespace PowerMode
{
    /// <summary>
    /// Adornment class that draws a square box in the top right hand corner of the viewport
    /// </summary>
    internal sealed class ExplosionViewportAdornment
    {
        private const int MinMsBetweenShakes = 10;

        [ThreadStatic]
        private static Random _random;

        /// <summary>
        /// The layer for the adornment.
        /// </summary>
        private readonly IAdornmentLayer _adornmentLayer;

        /// <summary>
        /// Text view to add the adornment on.
        /// </summary>
        private readonly IWpfTextView _view;

        private readonly ConcurrentBag<ExplosionParticle> _explosionParticles;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExplosionViewportAdornment"/> class.
        /// Creates a square image and attaches an event handler to the layout changed event that
        /// adds the the square in the upper right-hand corner of the TextView via the adornment layer
        /// </summary>
        /// <param name="view">The <see cref="IWpfTextView"/> upon which the adornment will be drawn</param>
        public ExplosionViewportAdornment(IWpfTextView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _view.TextBuffer.Changed += TextBuffer_Changed;
            _view.TextBuffer.PostChanged += TextBuffer_PostChanged;
            _adornmentLayer = view.GetAdornmentLayer("ExplosionViewportAdornment");
            _explosionParticles =
                new ConcurrentBag<ExplosionParticle>(
                    Enumerable.Repeat<Func<ExplosionParticle>>(NewParticle, (int)(ParticlePerPress * 3))
                        .Select(result => result()));
        }

        public static int ComboActivationThreshold { get; set; } = 0;
        public static int ComboTimeout { get; set; } = 10000;
        public static int ParticlePerPress { get; set; } = 10;
        public static int ParticlePartyChangeThreshold { get; set; } = 20;

        /// <summary>
        /// If set to true, enables particles all over the screen on large changes
        /// </summary>
        public static bool ParticlePartyEnabled { get; set; } = true;
        public static bool ParticlesEnabled { get; set; } = true;

        public static bool ShakeEnabled { get; set; } = true;

        public int ComboStreak { get; set; }
        public int ExplosionAmount { get; set; } = 2;

        public int ExplosionDelay { get; set; } = 50;

        /// <summary>
        /// Store the last time the user pressed a key.
        /// To maintain a combo, the user must keep pressing keys at least every x seconds.
        /// </summary>
        public DateTime LastKeyPress { get; set; } = DateTime.Now;

        public int MaxShakeAmount { get; set; } = 5;

        // In milliseconds
        private static Random Random => _random ?? (_random = new Random());

        public static bool PowerModeEnabled { get; set; } = true;

        /// <summary>
        /// Keep track of how many keypresses the user has done and returns whether power mode should be activated for each change.
        /// </summary>
        /// <returns>True if power mode should be activated for this change. False if power mode should be ignored for this change.</returns>
        private bool ComboCheck()
        {
            if (ComboActivationThreshold == 0)
            {
                LastKeyPress = DateTime.Now;
                return true;
            }
            var now = DateTime.Now;
            ComboStreak++;

            if (LastKeyPress.AddMilliseconds(ComboTimeout) < now) // More than x ms since last key-press. Combo has been broken.
            {
                ComboStreak = 1;
            }

            LastKeyPress = now;
            var activatePowerMode = ComboStreak >= ComboActivationThreshold; // Activate powermode if number of keypresses exceeds the threshold for activation
            return activatePowerMode; // Perhaps different levels for power-mode intensity? First just particles, then screen shake?
        }

        private void FormatCode(TextContentChangedEventArgs e)
        {
            if (!PowerModeEnabled)
            {
                return;
            }
            if ((DateTime.Now - LastKeyPress).TotalMilliseconds < MinMsBetweenShakes)
            {
                return;
            }

            if (e.Changes?.Count > 0)
            {
                HandleChange(e.Changes.Sum(x => x.Delta));
            }
        }

        private ExplosionParticle GetExplosionParticle()
        {
            if (_explosionParticles.TryTake(out var result))
            {
                return result;
            }
            return NewParticle();
        }

        private void HandleChange(int delta)
        {
            if (!PowerModeEnabled)
            {
                return;
            }
            if (ComboCheck())
            {
                delta = Math.Abs(delta);
                if (ParticlesEnabled)
                {
                    var top = _view.Caret.Top;
                    var left = _view.Caret.Left;
                    var partyMode = ParticlePartyEnabled && delta >= ParticlePartyChangeThreshold;
                    var particlesToCreate = ParticlePerPress;
                    if (partyMode)
                    {
                        particlesToCreate = 200;
                    }
                    for (uint i = 0; i < particlesToCreate; i++)
                    {

                        if (partyMode)
                        {
                            top = Random.Next((int)_view.ViewportTop, (int)_view.ViewportBottom);
                            left = Random.Next((int)_view.ViewportLeft, (int)_view.ViewportRight);
                        }
                        GetExplosionParticle().Explode(top, left);
                    }
                }
                if (ShakeEnabled)
                {
                    Shake(delta).ConfigureAwait(false);
                }
            }
        }

        private ExplosionParticle NewParticle()
        {
            var service = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
            return new ExplosionParticle(_adornmentLayer, service, particle => _explosionParticles.Add(particle));
        }

        private async Task Shake(int delta)
        {
            for (var i = 0; i < delta && i < MaxShakeAmount; i++)
            {
                int leftAmount = ExplosionAmount * Random.NextSignSwap(),
                    topAmount = ExplosionAmount * Random.NextSignSwap();

                _view.ViewportLeft += leftAmount;
                _view.ViewScroller.ScrollViewportVerticallyByPixels(topAmount);
                await Task.Delay(ExplosionDelay);
                _view.ViewportLeft -= leftAmount;
                _view.ViewScroller.ScrollViewportVerticallyByPixels(-topAmount);
            }
        }

        private void TextBuffer_Changed(object sender, TextContentChangedEventArgs e)
        {
            FormatCode(e);
        }

        private void TextBuffer_PostChanged(object sender, EventArgs e)
        {
        }
    }
}