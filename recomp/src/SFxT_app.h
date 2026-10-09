// SFxT - ReXGlue Recompiled Project
//
// Customize your app by overriding virtual hooks from rex::ReXApp.

#pragma once

#include <rex/rex_app.h>

#include "sfxt_profile.h"

class SfxtApp : public rex::ReXApp {
 public:
  using rex::ReXApp::ReXApp;

  static std::unique_ptr<rex::ui::WindowedApp> Create(
      rex::ui::WindowedAppContext& ctx) {
    return std::unique_ptr<SfxtApp>(new SfxtApp(ctx, "SFxT",
        PPCImageConfig));
  }

  void OnPreSetup(rex::RuntimeConfig& config) override {
    // The profile_* cvars must exist before the runtime parses SFxT.toml,
    // otherwise the file's profile lines are silently dropped. The macros
    // register at static-init time; this logs the values. The identity
    // overrides themselves are strong link-time replacements of the
    // runtime's XamUser* imports (see sfxt_profile.cpp).
    sfxt::RegisterProfileCVars();
  }

  // Override virtual hooks for customization:
  // void OnPostInitLogging() override {}
  // void OnLoadXexImage(std::string& xex_image) override {}
  // void OnPostLoadXexImage() override {}
  // void OnPostSetup() override {}
  // void OnCreateDialogs(rex::ui::ImGuiDrawer* drawer) override {}
  // std::unique_ptr<rex::ui::ImGuiDialog> CreateAchievementsOverlay() override;
  // std::unique_ptr<rex::ui::AchievementNotificationDialog>
  // CreateAchievementNotificationDialog() override;
  // void OnShutdown() override {}
  // void OnConfigurePaths(rex::PathConfig& paths) override {}
};
