// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.AppCenter.Analytics.Channel
{
    public interface ISessionTracker
    {
        void Resume();
        void Pause();
        void Stop();
        void DisableAutomaticSessionGeneration(bool isDisabled);
        void StartSession();
    }
}
