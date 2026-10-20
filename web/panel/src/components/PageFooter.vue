<!-- 页面底部操作栏（保存等）：固定在内容区底部，不随内容滚动。 -->
<template>
  <Teleport to="#page-footer" defer>
    <div class="surface mb-6 flex flex-wrap items-center gap-3 rounded-box px-4 py-3">
      <slot />
    </div>
  </Teleport>
</template>
