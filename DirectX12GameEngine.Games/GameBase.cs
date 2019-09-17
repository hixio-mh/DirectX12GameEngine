﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using DirectX12GameEngine.Core.Assets;
using Microsoft.Extensions.DependencyInjection;

namespace DirectX12GameEngine.Games
{
    public abstract class GameBase : IDisposable
    {
        private readonly object tickLock = new object();

        private bool isExiting;
        private readonly Stopwatch stopwatch = new Stopwatch();

        public GameBase(GameContext context)
        {
            Context = context;

            ServiceCollection services = new ServiceCollection();
            ConfigureServices(services);

            Services = services.BuildServiceProvider();

            Window = Services.GetService<GameWindow>();

            if (Window != null)
            {
                Window.TickRequested += (s, e) => Tick();
            }

            Content = Services.GetRequiredService<IContentManager>();
        }

        public IContentManager Content { get; }

        public IList<GameSystemBase> GameSystems { get; } = new List<GameSystemBase>();

        public GameContext Context { get; }

        public GameWindow? Window { get; private set; }

        public IServiceProvider Services { get; }

        public GameTime Time { get; } = new GameTime();

        public bool IsRunning { get; private set; }

        public virtual void Dispose()
        {
            foreach (GameSystemBase gameSystem in GameSystems)
            {
                gameSystem.Dispose();
            }

            Window?.Exit();
            Window = null;
        }

        public void Run()
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("This game is already running.");
            }

            IsRunning = true;

            Initialize();
            LoadContentAsync();

            stopwatch.Start();
            Time.Update(stopwatch.Elapsed, TimeSpan.Zero);

            BeginRun();

            Window?.Run();
        }

        public void Exit()
        {
            if (IsRunning)
            {
                isExiting = true;
                Window?.Exit();
            }
        }

        public void Tick()
        {
            try
            {
                lock (tickLock)
                {
                    if (isExiting)
                    {
                        CheckEndRun();
                        return;
                    }

                    TimeSpan elapsedTime = stopwatch.Elapsed - Time.Total;
                    Time.Update(stopwatch.Elapsed, elapsedTime);

                    Update(Time);

                    BeginDraw();
                    Draw(Time);
                }
            }
            finally
            {
                EndDraw();

                CheckEndRun();
            }
        }

        protected virtual void Initialize()
        {
            foreach (GameSystemBase gameSystem in GameSystems)
            {
                gameSystem.Initialize();
            }
        }

        protected virtual Task LoadContentAsync()
        {
            List<Task> loadingTasks = new List<Task>(GameSystems.Count);

            foreach (GameSystemBase gameSystem in GameSystems)
            {
                loadingTasks.Add(gameSystem.LoadContentAsync());
            }

            return Task.WhenAll(loadingTasks);
        }

        protected virtual void BeginRun()
        {
        }

        protected virtual void Update(GameTime gameTime)
        {
            foreach (GameSystemBase gameSystem in GameSystems)
            {
                gameSystem.Update(gameTime);
            }
        }

        protected virtual void BeginDraw()
        {
            foreach (GameSystemBase gameSystem in GameSystems)
            {
                gameSystem.BeginDraw();
            }
        }

        protected virtual void Draw(GameTime gameTime)
        {
            foreach (GameSystemBase gameSystem in GameSystems)
            {
                gameSystem.Draw(gameTime);
            }
        }

        protected virtual void EndDraw()
        {
            foreach (GameSystemBase gameSystem in GameSystems)
            {
                gameSystem.EndDraw();
            }
        }

        protected virtual void EndRun()
        {
        }

        protected virtual void ConfigureServices(IServiceCollection services)
        {
            Context.ConfigureServices(services);

            services.AddSingleton<GameBase>(this);
            services.AddSingleton<IContentManager, ContentManager>();
        }

        private void CheckEndRun()
        {
            if (isExiting && IsRunning)
            {
                EndRun();
                IsRunning = false;
                stopwatch.Stop();
            }
        }
    }
}
