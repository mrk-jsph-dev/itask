import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import BoardPresence from '../components/BoardPresence.vue'

const user = (id: string, name: string) => ({
  userId: id,
  displayName: name,
  avatarColor: '#1D5BF5',
})

describe('BoardPresence', () => {
  it('counts everyone on the board and shows up to three others', () => {
    const users = [
      user('me', 'Ada'),
      user('2', 'Bo'),
      user('3', 'Cy'),
      user('4', 'Di'),
      user('5', 'Ed'),
    ]
    const wrapper = mount(BoardPresence, { props: { users, meId: 'me', status: 'live' } })

    expect(wrapper.get('[data-testid=presence]').text()).toContain('5 people here')
    expect(wrapper.text()).toContain('+1')
    expect(wrapper.text()).toContain('you, Bo, Cy, Di, Ed')
  })

  it('stays hidden when nobody else is here', () => {
    const wrapper = mount(BoardPresence, {
      props: { users: [user('me', 'Ada')], meId: 'me', status: 'live' },
    })
    expect(wrapper.find('[data-testid=presence]').exists()).toBe(false)
  })

  it('shows when live updates are not getting through', () => {
    const wrapper = mount(BoardPresence, { props: { users: [], meId: 'me', status: 'offline' } })
    expect(wrapper.get('[data-testid=live-status]').text()).toContain('Offline')
  })
})
