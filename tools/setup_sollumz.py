import addon_utils


addon_utils.enable("Sollumz", default_set=True, persistent=True)

import Sollumz.dependencies as dependencies


ok = dependencies.install_dependencies(
    online_access_override=True,
    optional_dependencies_to_install={"pymateria"},
)
print(f"SOLLUMZ_DEPENDENCIES_INSTALLED={ok}")
