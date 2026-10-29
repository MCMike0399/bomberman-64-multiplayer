"""Primary APIs and the one explicitly evidenced Windows initialization fallback."""
DRIVERS={'windows-x64':'vulkan','linux-x64':'vulkan','macos-arm64':'metal'}
WINDOWS_FALLBACK_REASON='vulkan-initialization-unavailable'


def driver_matches(target,actual,requested=None,fallback_reason=None):
    if actual==DRIVERS[target]:return True
    return (target=='windows-x64' and actual=='d3d12' and
            (requested=='vulkan' and fallback_reason in (WINDOWS_FALLBACK_REASON,'previous-vulkan-startup-incomplete') or requested=='d3d12' and fallback_reason=='directx12-explicit-choice'))


def report_matches(target,report,key='renderingDriver'):
    return driver_matches(target,report.get(key),report.get('requestedDriver'),report.get('fallbackReason'))


def target_for_os(name):
    return {'Windows':'windows-x64','Linux':'linux-x64','macOS':'macos-arm64'}[name]
