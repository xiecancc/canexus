/**
 * 通用分页查询组合式函数：供列表页复用分页状态与搜索 / 重置 / 翻页逻辑。
 *
 * - query 为响应式查询条件（不含分页参数），由各页面自行声明字段
 * - fetcher 负责发起请求，入参为查询条件与分页参数的合并对象
 * - 内置请求序号竞态防护：连续触发查询时仅接受最新一次的结果
 * - 错误提示由请求层统一处理，此处仅保证 loading 复位
 */
import { reactive, ref } from 'vue'
import type { Ref } from 'vue'
import type { PagedRequest, PagedResult } from '@/api/types'

/** usePage 配置项 */
export interface UsePageOptions<Query extends object, Item> {
  /** 分页查询函数：入参为查询条件 + 分页参数 */
  fetcher: (params: Query & PagedRequest) => Promise<PagedResult<Item>>
  /** 查询条件默认值工厂（重置时重新生成，避免引用共享） */
  getDefaultQuery: () => Query
  /** 默认每页条数，缺省 10 */
  defaultPageSize?: number
  /** 是否在创建时立即加载首页数据，缺省 true */
  immediate?: boolean
}

/** 通用分页查询组合式函数 */
export function usePage<Query extends object, Item>(options: UsePageOptions<Query, Item>) {
  /** 加载状态 */
  const loading = ref(false)

  /** 页码（从 1 开始） */
  const pageIndex = ref(1)

  /** 每页条数 */
  const pageSize = ref(options.defaultPageSize ?? 10)

  /** 总条数 */
  const total = ref(0)

  /** 当前页数据 */
  const items: Ref<Item[]> = ref([])

  /** 查询条件（响应式） */
  const query = reactive(options.getDefaultQuery()) as Query

  /** 请求序号：仅接受最新一次请求的结果，防止快速翻页 / 频繁搜索时慢响应覆盖新数据 */
  let requestSeq = 0

  /** 按当前查询条件与分页参数加载数据 */
  async function fetchData(): Promise<void> {
    const seq = ++requestSeq
    loading.value = true
    try {
      const result = await options.fetcher({
        ...query,
        pageIndex: pageIndex.value,
        pageSize: pageSize.value,
      })
      // 竞态防护：已有更新的请求发起，丢弃本次过期结果
      if (seq !== requestSeq) {
        return
      }
      items.value = result.items
      total.value = result.total
    } finally {
      // 仅由最新一次请求控制 loading（过期请求不干扰最新请求的加载状态）
      if (seq === requestSeq) {
        loading.value = false
      }
    }
  }

  /** 搜索：回到第一页并加载 */
  function handleSearch(): void {
    pageIndex.value = 1
    void fetchData()
  }

  /** 重置：恢复默认查询条件并搜索 */
  function handleReset(): void {
    Object.assign(query, options.getDefaultQuery())
    handleSearch()
  }

  /** 每页条数变化：回到第一页并加载 */
  function handleSizeChange(): void {
    pageIndex.value = 1
    void fetchData()
  }

  /** 页码变化：加载对应页数据 */
  function handlePageChange(): void {
    void fetchData()
  }

  if (options.immediate !== false) {
    void fetchData()
  }

  return {
    loading,
    pageIndex,
    pageSize,
    total,
    items,
    query,
    fetchData,
    handleSearch,
    handleReset,
    handleSizeChange,
    handlePageChange,
  }
}
