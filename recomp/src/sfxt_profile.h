// sfxt - Synthetic dashboard profile.
//
// The guest's identity imports (XamUserGetXUID / GetName / GetSigninState /
// GetSigninInfo) are answered by this module: it defines strong extern "C"
// overrides of the runtime's __imp__ import implementations, which the static
// recomp's generated call sites link against directly (see sfxt_profile.cpp).
//
// Scope, stated plainly: this gives the game a stable local identity that
// survives restarts. It is NOT an Xbox Live account and authenticates to
// nothing.

#pragma once

#include <cstdint>
#include <string>

namespace sfxt {

// Guest-visible profile identity. Defaults preserve the runtime's built-in
// behaviour ("User" / 0xB13EBABEBABEBABE) so nothing changes until a value
// is set.
struct Profile {
  std::string name;
  uint64_t xuid = 0;
  bool signed_in = false;
};

// Defines the profile_* cvars. Must run before the cvar TOML is parsed so the
// file's values bind to them - the app calls this from OnPreSetup.
void RegisterProfileCVars();

// Current profile, as read from the cvars.
Profile GetProfile();

}  // namespace sfxt
