import { createContext, useContext } from 'react'

import { WebUserDto } from './api'

export const UserContext = createContext<WebUserDto | null>(null)

export const useUser = () => useContext(UserContext)
