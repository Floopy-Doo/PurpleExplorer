﻿namespace PurpleExplorer.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Helpers;
    using ReactiveUI;

    public class MoveMessageWindowViewModal : DialogViewModelBase
    {
        private IReadOnlyList<string> _queues;
        private string _queueTopicName;
    
        public string QueueTopicName
        {
            get => this._queueTopicName;
            set => this.RaiseAndSetIfChanged(ref this._queueTopicName, value);
        }
        
        public IReadOnlyList<string> Queues
        {
            get => this._queues;
            set => this.RaiseAndSetIfChanged(ref this._queues, value);
        }

        public MoveMessageWindowViewModal(IReadOnlyList<string> queues = null)
        {
            this.Queues = queues ?? Array.Empty<string>();
        }

        public async Task MoveMessage(Window window)
        {

            if (string.IsNullOrEmpty(this.QueueTopicName))
            {
                await MessageBoxHelper.ShowError("Please enter a queue/topic to be sent to");
                return;
            }
            
            this.Cancel = false;
            window.Close();
        }
    }
}