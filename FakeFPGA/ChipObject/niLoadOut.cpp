#include "NetworkCommunication/LoadOut.h"
#include <stdint.h>

namespace nLoadOut {
bool getModulePresence(tModuleType moduleType, uint8_t moduleNumber) {
	switch (moduleType) {
	case kModuleType_Analog:
		return moduleNumber ==0;
	case kModuleType_Digital:
		return moduleNumber <= 1 && moduleNumber >= 0;
	case kModuleType_Solenoid:
		return moduleNumber == 0;
	default:
		return false;
	}
}
}
