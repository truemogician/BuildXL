// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#include "stdafx.h"

#include "DetouredScope.h"

// ----------------------------------------------------------------------------
// GLOBALS
// ----------------------------------------------------------------------------

__declspec(thread) size_t DetouredScope::gt_DetouredCount = 0;
